using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;

namespace Dms.IntegrationTests;

/// <summary>
/// Phase 4 exit criteria (docs/architecture.md section 10): transitions, workflow version
/// isolation, approval tied to a version, a new version after approval, cancelling superseded
/// runs, and the self-approval block. Everything goes through the public API.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class WorkflowApiTests(DmsApiFactory factory)
{
    private static object Step(
        string code,
        int sequence,
        Guid? userId = null,
        string assigneeType = "User",
        Guid? assigneeId = null,
        string completionRule = "Any",
        object? condition = null,
        bool allowSelfApproval = false,
        bool isRequired = true,
        object[]? actions = null) => new
        {
            code,
            name = $"مرحله {code}",
            sequence,
            assigneeType,
            assigneeId = assigneeId ?? userId,
            completionRule,
            isRequired,
            allowSelfApproval,
            condition,
            actions = actions ?? new object[]
            {
                new { action = "Approve" },
                new { action = "Reject", commentRequired = true },
                new { action = "Return" },
                new { action = "RequestChanges" },
                new { action = "Forward" },
            },
        };

    private sealed record Setup(HttpClient Admin, HttpClient Author, Guid AuthorId, Guid CategoryId, Guid TypeId, Guid WorkflowId);

    /// <summary>A published workflow with the given steps, and a type (with an amount field) that uses it.</summary>
    private async Task<Setup> ArrangeAsync(string mode, params object[] steps)
    {
        var admin = await factory.AdminAsync();
        var (author, authorId) = await factory.CreateUserAsync("wf.author");
        var categoryId = await admin.CreateCategoryAsync();
        await admin.GrantManyAsync("Category", categoryId, authorId, "DOCUMENT_VIEW", "DOCUMENT_CREATE");

        var workflowId = await CreateWorkflowAsync(admin, steps);

        var code = $"W{Guid.NewGuid():N}"[..16].ToUpperInvariant();
        var type = await admin.PostAsJsonAsync("/api/v1/admin/document-types", new { code, name = "نامه", description = (string?)null });
        var typeId = (await type.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await admin.PutAsJsonAsync($"/api/v1/admin/document-types/{typeId}/draft", new
        {
            fields = new[] { new { code = "amount", label = new { fa = "مبلغ" }, type = "Decimal" } },
            rules = Array.Empty<object>(),
        })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.PostAsync($"/api/v1/admin/document-types/{typeId}/publish", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.PutAsJsonAsync($"/api/v1/admin/document-types/{typeId}", new
        {
            name = "نامه",
            description = (string?)null,
            settings = new { workflowId, workflowMode = mode },
        })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        return new Setup(admin, author, authorId, categoryId, typeId, workflowId);
    }

    private static async Task<Guid> CreateWorkflowAsync(HttpClient admin, object[] steps)
    {
        var code = $"F{Guid.NewGuid():N}"[..16].ToUpperInvariant();
        var created = await admin.PostAsJsonAsync("/api/v1/admin/workflows", new { code, name = "تأیید", description = (string?)null });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var saved = await admin.PutAsJsonAsync($"/api/v1/admin/workflows/{id}/draft", new { steps });
        saved.StatusCode.ShouldBe(HttpStatusCode.NoContent, await saved.Content.ReadAsStringAsync());
        (await admin.PostAsync($"/api/v1/admin/workflows/{id}/publish", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        return id;
    }

    private static async Task<(Guid DocumentId, Guid VersionId)> FileAsync(Setup setup, decimal amount = 1)
    {
        var upload = await setup.Author.UploadAsync(DocumentTestKit.Pdf($"wf {Guid.NewGuid()}"));
        var response = await setup.Author.PostAsJsonAsync("/api/v1/documents", new
        {
            title = "نامه‌ی اداری",
            categoryId = setup.CategoryId,
            documentTypeId = setup.TypeId,
            uploadId = upload.GetProperty("uploadId").GetGuid(),
            metadata = new { amount },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("documentId").GetGuid(), body.GetProperty("versionId").GetGuid());
    }

    private static async Task<Guid> AddVersionAsync(Setup setup, Guid documentId)
    {
        var response = await setup.Author.AddVersionRawAsync(documentId, DocumentTestKit.Pdf($"v {Guid.NewGuid()}"));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("versionId").GetGuid();
    }

    private static async Task StartAsync(HttpClient client, Guid documentId, Guid versionId)
    {
        var started = await client.PostAsync($"/api/v1/documents/{documentId}/versions/{versionId}/workflow/start", null);
        started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement> TaskForAsync(HttpClient reviewer, Guid documentId)
    {
        var tasks = await reviewer.GetFromJsonAsync<JsonElement>("/api/v1/workflow/tasks");
        return tasks.EnumerateArray().Single(task => task.GetProperty("documentId").GetGuid() == documentId);
    }

    private static async Task<HttpResponseMessage> ActAsync(HttpClient reviewer, Guid taskId, string action, object? body = null) =>
        await reviewer.PostAsJsonAsync($"/api/v1/workflow/tasks/{taskId}/{action}", body ?? new { comment = (string?)null });

    private static async Task<JsonElement> InstancesAsync(HttpClient client, Guid documentId) =>
        await client.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}/workflow");

    private static async Task<JsonElement> VersionAsync(HttpClient client, Guid documentId, Guid versionId) =>
        (await client.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}/versions"))
            .EnumerateArray().Single(version => version.GetProperty("id").GetGuid() == versionId);

    [Fact]
    public async Task A_draft_is_reviewed_and_approval_lands_on_that_version()
    {
        var (reviewer, reviewerId) = await factory.CreateUserAsync("wf.reviewer");
        var setup = await ArrangeAsync("Manual", Step("legal", 1, reviewerId));
        var (documentId, v1) = await FileAsync(setup);

        // Manual mode: a draft, invisible to plain readers, with no effective version yet.
        (await VersionAsync(setup.Author, documentId, v1)).GetProperty("approvalStatus").GetString().ShouldBe("Draft");
        (await reviewer.GetAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await StartAsync(setup.Author, documentId, v1);

        // The open task lets the reviewer see the document, drafts included (section 5.4 step 8).
        (await reviewer.GetAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var task = await TaskForAsync(reviewer, documentId);
        task.GetProperty("stepCode").GetString().ShouldBe("legal");

        (await ActAsync(reviewer, task.GetProperty("id").GetGuid(), "approve")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var details = await setup.Author.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}");
        details.GetProperty("effectiveVersionId").GetGuid().ShouldBe(v1);
        (await VersionAsync(setup.Author, documentId, v1)).GetProperty("approvalStatus").GetString().ShouldBe("Approved");
        (await InstancesAsync(setup.Author, documentId))[0].GetProperty("status").GetString().ShouldBe("Approved");

        // The task is gone, and with it the reviewer's temporary access.
        (await reviewer.GetAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await factory.ShouldHaveAuditAsync("WORKFLOW_COMPLETED");
    }

    [Fact]
    public async Task Return_sends_the_review_back_and_forward_hands_it_on()
    {
        var (first, firstId) = await factory.CreateUserAsync("wf.first");
        var (second, secondId) = await factory.CreateUserAsync("wf.second");
        var (deputy, deputyId) = await factory.CreateUserAsync("wf.deputy");
        var setup = await ArrangeAsync("Manual", Step("check", 1, firstId), Step("sign", 2, secondId));
        var (documentId, v1) = await FileAsync(setup);
        await StartAsync(setup.Author, documentId, v1);

        (await ActAsync(first, (await TaskForAsync(first, documentId)).GetProperty("id").GetGuid(), "approve"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The signer returns it to the checker for another look, without a content change.
        var signTask = await TaskForAsync(second, documentId);
        (await ActAsync(second, signTask.GetProperty("id").GetGuid(), "return", new { comment = "بند ۳ را دوباره ببینید" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var again = await TaskForAsync(first, documentId);
        again.GetProperty("stepCode").GetString().ShouldBe("check");

        // The checker forwards it to a deputy, who approves.
        (await ActAsync(first, again.GetProperty("id").GetGuid(), "forward", new { forwardToUserId = deputyId }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await first.GetFromJsonAsync<JsonElement>("/api/v1/workflow/tasks")).EnumerateArray()
            .Any(task => task.GetProperty("documentId").GetGuid() == documentId).ShouldBeFalse();
        (await ActAsync(deputy, (await TaskForAsync(deputy, documentId)).GetProperty("id").GetGuid(), "approve"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ActAsync(second, (await TaskForAsync(second, documentId)).GetProperty("id").GetGuid(), "approve"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var instance = (await InstancesAsync(setup.Author, documentId))[0];
        instance.GetProperty("status").GetString().ShouldBe("Approved");
        instance.GetProperty("tasks").EnumerateArray().Select(task => task.GetProperty("action").GetString())
            .ShouldBe(["Approve", "Return", "Forward", "Approve", "Approve"]);
        await factory.ShouldHaveAuditAsync("WORKFLOW_RETURNED");
        await factory.ShouldHaveAuditAsync("WORKFLOW_FORWARDED");
    }

    [Fact]
    public async Task Reject_needs_a_comment_and_ends_the_review()
    {
        var (reviewer, reviewerId) = await factory.CreateUserAsync("wf.rejecter");
        var setup = await ArrangeAsync("Manual", Step("legal", 1, reviewerId));
        var (documentId, v1) = await FileAsync(setup);
        await StartAsync(setup.Author, documentId, v1);
        var taskId = (await TaskForAsync(reviewer, documentId)).GetProperty("id").GetGuid();

        var silent = await ActAsync(reviewer, taskId, "reject");
        silent.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await ActAsync(reviewer, taskId, "reject", new { comment = "مبلغ اشتباه است" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await VersionAsync(setup.Author, documentId, v1)).GetProperty("approvalStatus").GetString().ShouldBe("Rejected");

        // Finished: the same task cannot be acted on again, and a rejected version cannot restart.
        (await ActAsync(reviewer, taskId, "approve")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await setup.Author.PostAsync($"/api/v1/documents/{documentId}/versions/{v1}/workflow/start", null))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Requested_changes_are_answered_with_a_new_version_that_is_reviewed_on_its_own()
    {
        var (reviewer, reviewerId) = await factory.CreateUserAsync("wf.changes");
        var setup = await ArrangeAsync("AutoOnVersion", Step("legal", 1, reviewerId));
        var (documentId, v1) = await FileAsync(setup);

        // AUTO: the workflow started with the version.
        var first = await TaskForAsync(reviewer, documentId);
        (await ActAsync(reviewer, first.GetProperty("id").GetGuid(), "request-changes", new { comment = "امضا ندارد" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await VersionAsync(setup.Author, documentId, v1)).GetProperty("approvalStatus").GetString().ShouldBe("ChangesRequested");

        var v2 = await AddVersionAsync(setup, documentId);
        var second = await TaskForAsync(reviewer, documentId);
        second.GetProperty("versionId").GetGuid().ShouldBe(v2);
        (await ActAsync(reviewer, second.GetProperty("id").GetGuid(), "approve")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // History is not rewritten: V1 keeps its outcome, V2 is approved and effective (D7).
        (await VersionAsync(setup.Author, documentId, v1)).GetProperty("approvalStatus").GetString().ShouldBe("ChangesRequested");
        (await setup.Author.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}"))
            .GetProperty("effectiveVersionId").GetGuid().ShouldBe(v2);
    }

    [Fact]
    public async Task A_new_version_after_approval_leaves_the_approved_one_effective_until_it_is_approved()
    {
        var (reviewer, reviewerId) = await factory.CreateUserAsync("wf.next");
        var (reader, readerId) = await factory.CreateUserAsync("wf.reader");
        var setup = await ArrangeAsync("AutoOnVersion", Step("legal", 1, reviewerId));
        await setup.Admin.GrantManyAsync("Category", setup.CategoryId, readerId, "DOCUMENT_VIEW", "DOCUMENT_DOWNLOAD");

        var v1Bytes = DocumentTestKit.Pdf($"approved {Guid.NewGuid()}");
        var upload = await setup.Author.UploadAsync(v1Bytes);
        var created = await setup.Author.PostAsJsonAsync("/api/v1/documents", new
        {
            title = "بخشنامه",
            categoryId = setup.CategoryId,
            documentTypeId = setup.TypeId,
            uploadId = upload.GetProperty("uploadId").GetGuid(),
            metadata = new { amount = 1 },
        });
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var documentId = body.GetProperty("documentId").GetGuid();
        var v1 = body.GetProperty("versionId").GetGuid();
        (await ActAsync(reviewer, (await TaskForAsync(reviewer, documentId)).GetProperty("id").GetGuid(), "approve"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var v2 = await AddVersionAsync(setup, documentId);

        // While V2 is in review, readers keep getting V1 (section 6.7).
        var details = await setup.Author.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}");
        details.GetProperty("currentVersionId").GetGuid().ShouldBe(v2);
        details.GetProperty("effectiveVersionId").GetGuid().ShouldBe(v1);
        (await reader.GetByteArrayAsync($"/api/v1/documents/{documentId}/content")).ShouldBe(v1Bytes);

        // And the draft itself stays out of a plain reader's reach (decision D6).
        (await reader.GetAsync($"/api/v1/documents/{documentId}/versions/{v2}/content")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await ActAsync(reviewer, (await TaskForAsync(reviewer, documentId)).GetProperty("id").GetGuid(), "approve"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await setup.Author.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}"))
            .GetProperty("effectiveVersionId").GetGuid().ShouldBe(v2);
        (await VersionAsync(setup.Author, documentId, v1)).GetProperty("approvalStatus").GetString().ShouldBe("Approved");
    }

    [Fact]
    public async Task A_newer_approval_cancels_older_runs_but_a_new_version_alone_does_not()
    {
        var (reviewer, reviewerId) = await factory.CreateUserAsync("wf.supersede");
        var setup = await ArrangeAsync("AutoOnVersion", Step("legal", 1, reviewerId));
        var (documentId, v1) = await FileAsync(setup);
        var v2 = await AddVersionAsync(setup, documentId);

        // Decision D7: creating V2 leaves V1's review running.
        var runs = await InstancesAsync(setup.Author, documentId);
        runs.EnumerateArray().Count(run => run.GetProperty("status").GetString() == "Running").ShouldBe(2);

        var tasks = (await reviewer.GetFromJsonAsync<JsonElement>("/api/v1/workflow/tasks")).EnumerateArray()
            .Where(task => task.GetProperty("documentId").GetGuid() == documentId).ToList();
        var v2Task = tasks.Single(task => task.GetProperty("versionId").GetGuid() == v2);
        (await ActAsync(reviewer, v2Task.GetProperty("id").GetGuid(), "approve")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Approving V2 supersedes V1's run: approving V1 now could only move readers backwards.
        runs = await InstancesAsync(setup.Author, documentId);
        var v1Run = runs.EnumerateArray().Single(run => run.GetProperty("versionId").GetGuid() == v1);
        v1Run.GetProperty("status").GetString().ShouldBe("Cancelled");
        v1Run.GetProperty("cancelReason").GetString()!.ShouldContain("V2.1");
        (await VersionAsync(setup.Author, documentId, v1)).GetProperty("approvalStatus").GetString().ShouldBe("Cancelled");
        (await setup.Author.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}"))
            .GetProperty("effectiveVersionId").GetGuid().ShouldBe(v2);
    }

    [Fact]
    public async Task A_running_instance_stays_on_the_workflow_version_it_started_with()
    {
        var (first, firstId) = await factory.CreateUserAsync("wf.iso1");
        var (second, secondId) = await factory.CreateUserAsync("wf.iso2");
        var setup = await ArrangeAsync("Manual", Step("legal", 1, firstId));
        var (oldDoc, oldVersion) = await FileAsync(setup);
        await StartAsync(setup.Author, oldDoc, oldVersion);

        // Workflow v2 adds a second signature.
        (await setup.Admin.PutAsJsonAsync($"/api/v1/admin/workflows/{setup.WorkflowId}/draft", new
        {
            steps = new[] { Step("legal", 1, firstId), Step("board", 2, secondId) },
        })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await setup.Admin.PostAsync($"/api/v1/admin/workflows/{setup.WorkflowId}/publish", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The old run still has one step: approving it finishes the review.
        (await ActAsync(first, (await TaskForAsync(first, oldDoc)).GetProperty("id").GetGuid(), "approve"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await InstancesAsync(setup.Author, oldDoc))[0].GetProperty("status").GetString().ShouldBe("Approved");

        // A new run follows v2 and waits for the board.
        var (newDoc, newVersion) = await FileAsync(setup);
        await StartAsync(setup.Author, newDoc, newVersion);
        (await ActAsync(first, (await TaskForAsync(first, newDoc)).GetProperty("id").GetGuid(), "approve"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await TaskForAsync(second, newDoc)).GetProperty("stepCode").GetString().ShouldBe("board");

        // And the published definition itself cannot be edited underneath the old run.
        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE workflow.workflow_steps SET name = 'hacked'
             WHERE workflow_version_id = (SELECT workflow_version_id FROM workflow.workflow_instances
                                           WHERE document_version_id = @v)
            """;
        command.Parameters.AddWithValue("v", oldVersion);
        await Should.ThrowAsync<Npgsql.PostgresException>(() => command.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task The_author_cannot_approve_their_own_version()
    {
        var (colleague, colleagueId) = await factory.CreateUserAsync("wf.colleague");
        var admin = await factory.AdminAsync();
        var group = await admin.PostAsJsonAsync("/api/v1/admin/groups", new { code = $"G{Guid.NewGuid():N}"[..12], name = "حقوقی" });
        var groupId = (await group.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var setup = await ArrangeAsync("Manual", Step("team", 1, assigneeType: "Group", assigneeId: groupId));
        foreach (var member in new[] { setup.AuthorId, colleagueId })
        {
            (await admin.PutAsync($"/api/v1/admin/groups/{groupId}/members/{member}", null)).IsSuccessStatusCode.ShouldBeTrue();
        }

        var (documentId, v1) = await FileAsync(setup);
        await StartAsync(setup.Author, documentId, v1);

        // The author is in the group, sees the shared task, and is still refused.
        var task = await TaskForAsync(setup.Author, documentId);
        var own = await ActAsync(setup.Author, task.GetProperty("id").GetGuid(), "approve");
        own.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await own.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("workflow.self_approval");

        (await ActAsync(colleague, task.GetProperty("id").GetGuid(), "approve")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_step_only_the_author_could_take_waits_for_an_administrator()
    {
        var setup = await ArrangeAsync("Manual", Step("self", 1, assigneeType: "Creator"));
        var (documentId, v1) = await FileAsync(setup);
        await StartAsync(setup.Author, documentId, v1);

        var run = (await InstancesAsync(setup.Author, documentId))[0];
        run.GetProperty("status").GetString().ShouldBe("Running");
        run.GetProperty("attentionReason").GetString()!.ShouldContain("self");
        run.GetProperty("tasks").GetArrayLength().ShouldBe(0);
        await factory.ShouldHaveAuditAsync("WORKFLOW_NEEDS_ATTENTION");

        // The initiator can cancel it, and the draft may start again later.
        (await setup.Author.PostAsJsonAsync($"/api/v1/workflow/instances/{run.GetProperty("id").GetGuid()}/cancel", new { reason = "اشتباه" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await VersionAsync(setup.Author, documentId, v1)).GetProperty("approvalStatus").GetString().ShouldBe("Cancelled");
    }

    [Fact]
    public async Task Conditions_skip_steps_and_all_steps_need_every_member()
    {
        var (a, aId) = await factory.CreateUserAsync("wf.all.a");
        var (b, bId) = await factory.CreateUserAsync("wf.all.b");
        var (board, boardId) = await factory.CreateUserAsync("wf.board");
        var admin = await factory.AdminAsync();
        var group = await admin.PostAsJsonAsync("/api/v1/admin/groups", new { code = $"G{Guid.NewGuid():N}"[..12], name = "کمیته" });
        var groupId = (await group.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        foreach (var member in new[] { aId, bId })
        {
            await admin.PutAsync($"/api/v1/admin/groups/{groupId}/members/{member}", null);
        }

        var setup = await ArrangeAsync(
            "Manual",
            Step("committee", 1, assigneeType: "Group", assigneeId: groupId, completionRule: "All"),
            Step("board", 2, boardId, condition: new { field = "amount", op = "gt", value = 10_000_000_000m }));

        // A small amount: the board step is skipped.
        var (small, smallV1) = await FileAsync(setup, amount: 5);
        await StartAsync(setup.Author, small, smallV1);
        (await ActAsync(a, (await TaskForAsync(a, small)).GetProperty("id").GetGuid(), "approve")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // ALL: one member is not enough.
        (await InstancesAsync(setup.Author, small))[0].GetProperty("status").GetString().ShouldBe("Running");
        (await ActAsync(b, (await TaskForAsync(b, small)).GetProperty("id").GetGuid(), "approve")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var run = (await InstancesAsync(setup.Author, small))[0];
        run.GetProperty("status").GetString().ShouldBe("Approved");
        run.GetProperty("skippedSteps").EnumerateArray().Select(step => step.GetString()).ShouldBe(["board"]);

        // A large amount reaches the board.
        var (large, largeV1) = await FileAsync(setup, amount: 12_000_000_000m);
        await StartAsync(setup.Author, large, largeV1);
        foreach (var member in new[] { a, b })
        {
            await ActAsync(member, (await TaskForAsync(member, large)).GetProperty("id").GetGuid(), "approve");
        }

        (await TaskForAsync(board, large)).GetProperty("stepCode").GetString().ShouldBe("board");
    }

    [Fact]
    public async Task Two_members_approving_one_shared_task_at_once_get_one_success_and_one_conflict()
    {
        var (a, aId) = await factory.CreateUserAsync("wf.race.a");
        var (b, bId) = await factory.CreateUserAsync("wf.race.b");
        var admin = await factory.AdminAsync();
        var group = await admin.PostAsJsonAsync("/api/v1/admin/groups", new { code = $"G{Guid.NewGuid():N}"[..12], name = "دبیرخانه" });
        var groupId = (await group.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await admin.PutAsync($"/api/v1/admin/groups/{groupId}/members/{aId}", null);
        await admin.PutAsync($"/api/v1/admin/groups/{groupId}/members/{bId}", null);

        var setup = await ArrangeAsync("Manual", Step("desk", 1, assigneeType: "Group", assigneeId: groupId));
        var (documentId, v1) = await FileAsync(setup);
        await StartAsync(setup.Author, documentId, v1);
        var taskId = (await TaskForAsync(a, documentId)).GetProperty("id").GetGuid();

        var results = await Task.WhenAll(ActAsync(a, taskId, "approve"), ActAsync(b, taskId, "approve"));

        results.Select(result => result.StatusCode).Order()
            .ShouldBe([HttpStatusCode.NoContent, HttpStatusCode.Conflict]);
    }

    [Fact]
    public async Task An_explicit_deny_beats_the_task()
    {
        var (reviewer, reviewerId) = await factory.CreateUserAsync("wf.denied");
        var setup = await ArrangeAsync("Manual", Step("legal", 1, reviewerId));
        var (documentId, v1) = await FileAsync(setup);
        await StartAsync(setup.Author, documentId, v1);
        await setup.Admin.GrantAsync("Document", documentId, reviewerId, "WORKFLOW_APPROVE", effect: "Deny");

        var taskId = (await TaskForAsync(reviewer, documentId)).GetProperty("id").GetGuid();

        (await ActAsync(reviewer, taskId, "approve")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_type_set_to_workflow_refuses_documents_while_the_workflow_is_unusable()
    {
        var setup = await ArrangeAsync("AutoOnVersion", Step("legal", 1, Guid.NewGuid()));
        (await setup.Admin.PutAsJsonAsync($"/api/v1/admin/workflows/{setup.WorkflowId}", new { name = "تأیید", isActive = false }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var upload = await setup.Author.UploadAsync(DocumentTestKit.Pdf("orphan"));
        var response = await setup.Author.PostAsJsonAsync("/api/v1/documents", new
        {
            title = "بدون گردش کار",
            categoryId = setup.CategoryId,
            documentTypeId = setup.TypeId,
            uploadId = upload.GetProperty("uploadId").GetGuid(),
            metadata = new { amount = 1 },
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("workflow.not_published");
    }

    [Fact]
    public async Task Invalid_designs_are_refused_by_path()
    {
        var admin = await factory.AdminAsync();
        var created = await admin.PostAsJsonAsync("/api/v1/admin/workflows", new { code = $"X{Guid.NewGuid():N}"[..12], name = "x" });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var saved = await admin.PutAsJsonAsync($"/api/v1/admin/workflows/{id}/draft", new
        {
            steps = new object[]
            {
                new { code = "a", name = "A", sequence = 1, assigneeType = "Group", actions = new[] { new { action = "Reject" } } },
                new { code = "b", name = "B", sequence = 2, assigneeType = "Creator", condition = new { field = "x", op = "like", value = 1 },
                      actions = new object[] { new { action = "Approve" }, new { action = "Return", targetStepCode = "zzz" } } },
            },
        });

        saved.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        errors.EnumerateObject().Select(property => property.Name).Order()
            .ShouldBe(["steps[0].actions", "steps[0].assigneeId", "steps[1].actions", "steps[1].condition"]);
    }
}
