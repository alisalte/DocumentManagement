using Dms.SharedKernel;
using Dms.SharedKernel.Rules;
using Dms.Workflow.Contracts;
using Dms.Workflow.Domain;

namespace Dms.Workflow.Application;

/// <summary>
/// Checks a workflow design on save and on publish, so a published graph is always walkable:
/// unique step codes, assignees that match their type, conditions that parse, every step able to
/// approve, and RETURN targets that exist earlier in the flow. Errors are keyed by path
/// ("steps[1].assigneeId") for the editor.
/// </summary>
public static class StepDesignValidator
{
    public const int MaxSteps = 50;

    public static Result Validate(IReadOnlyList<WorkflowStepSchema> steps)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        void Add(string path, string message)
        {
            if (!errors.TryGetValue(path, out var list))
            {
                errors[path] = list = [];
            }

            list.Add(message);
        }

        if (steps.Count == 0)
        {
            Add("steps", "A workflow needs at least one step.");
        }

        if (steps.Count > MaxSteps)
        {
            Add("steps", $"At most {MaxSteps} steps.");
        }

        var codes = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            var path = $"steps[{index}]";

            if (step.Code is null || !RuleParser.IsValidFieldCode(step.Code))
            {
                Add($"{path}.code", "Use lower-case letters, digits and underscores, starting with a letter.");
            }
            else if (!codes.TryAdd(step.Code, step.Sequence))
            {
                Add($"{path}.code", $"The code '{step.Code}' is used twice.");
            }

            if (string.IsNullOrWhiteSpace(step.Name) || step.Name.Length > 200)
            {
                Add($"{path}.name", "A name of at most 200 characters is required.");
            }

            if (step.Sequence is < 1 or > 1000)
            {
                Add($"{path}.sequence", "The sequence is a number from 1 to 1000.");
            }

            var needsId = step.AssigneeType is AssigneeType.User or AssigneeType.Group or AssigneeType.Role;
            if (needsId != (step.AssigneeId is not null))
            {
                Add($"{path}.assigneeId", needsId
                    ? "Pick the user, group or role."
                    : "This assignee type is resolved when the step starts; no fixed assignee.");
            }

            var needsField = step.AssigneeType == AssigneeType.DynamicUserField;
            if (needsField != (step.AssigneeFieldCode is not null)
                || (step.AssigneeFieldCode is { } field && !RuleParser.IsValidFieldCode(field)))
            {
                Add($"{path}.assigneeFieldCode", needsField
                    ? "Name the USER field that holds the assignee."
                    : "Only a dynamic-user step reads a field.");
            }

            if (step.SlaHours is < 1 or > 8760)
            {
                Add($"{path}.slaHours", "The SLA is 1 to 8760 hours.");
            }

            if (step.Condition is { } condition && RuleParser.Parse(condition) is { IsFailure: true } parsed)
            {
                Add($"{path}.condition", parsed.Error.Message);
            }

            var actions = step.Actions ?? [];
            if (actions.All(action => action.Action != WorkflowAction.Approve))
            {
                Add($"{path}.actions", "Every step must allow APPROVE, or the workflow could never finish.");
            }

            if (actions.GroupBy(action => action.Action).Any(group => group.Count() > 1))
            {
                Add($"{path}.actions", "An action is listed twice.");
            }
        }

        // Targets are checked once every code is known.
        for (var index = 0; index < steps.Count; index++)
        {
            foreach (var action in (steps[index].Actions ?? []).Where(action => action.TargetStepCode is not null))
            {
                if (action.Action != WorkflowAction.Return)
                {
                    Add($"steps[{index}].actions", "Only RETURN has a target step.");
                }
                else if (!codes.TryGetValue(action.TargetStepCode!, out var targetSequence))
                {
                    Add($"steps[{index}].actions", $"RETURN goes to '{action.TargetStepCode}', which is not a step.");
                }
                else if (targetSequence >= steps[index].Sequence)
                {
                    Add($"steps[{index}].actions", "RETURN must go to an earlier step.");
                }
            }
        }

        return errors.Count == 0
            ? Result.Success()
            : Result.Failure(Error.ValidationFields(
                "workflow.invalid",
                "The workflow has errors.",
                errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray())));
    }
}
