using Dms.Application;
using Dms.Audit.Contracts;
using Dms.Identity.Domain;
using Dms.SharedKernel;
using Microsoft.Extensions.Options;

namespace Dms.Identity.Application;

public sealed record AuthenticatedUserDto(
    Guid Id,
    string Username,
    string DisplayName,
    bool IsSystemAdmin,
    bool MustChangePassword);

public sealed record AuthTokens(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    AuthenticatedUserDto User);

public sealed record LoginCommand(string Username, string Password) : ICommand<Result<AuthTokens>>;

public sealed record RefreshTokenCommand(string RefreshToken) : ICommand<Result<AuthTokens>>;

public sealed record LogoutCommand(string RefreshToken) : ICommand<Result>;

public sealed record ChangeOwnPasswordCommand(string CurrentPassword, string NewPassword) : ICommand<Result>;

internal static class IdentityErrors
{
    /// <summary>
    /// One message for every failure mode of login. Distinguishing "unknown user" from "wrong
    /// password" would let an attacker enumerate accounts.
    /// </summary>
    public static readonly Error InvalidCredentials =
        Error.Unauthorized("auth.invalid_credentials", "The username or password is incorrect.");

    public static readonly Error AccountLocked = Error.Unauthorized(
        "auth.account_locked",
        "The account is temporarily locked after too many failed attempts.");

    public static readonly Error InvalidRefreshToken =
        Error.Unauthorized("auth.invalid_refresh_token", "The refresh token is not valid.");

    public static readonly Error Unauthenticated =
        Error.Unauthorized("auth.unauthenticated", "The request is not authenticated.");

    public static readonly Error UserNotFound = Error.NotFound("user.not_found", "The user does not exist.");
}

public sealed class LoginHandler(
    IUserRepository users,
    IUserSessionRepository sessions,
    IPasswordHasher passwordHasher,
    IAccessTokenIssuer tokenIssuer,
    ISecureTokenGenerator tokenGenerator,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IOptions<IdentityModuleOptions> options,
    TimeProvider timeProvider) : ICommandHandler<LoginCommand, Result<AuthTokens>>
{
    public async Task<Result<AuthTokens>> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var settings = options.Value;
        var user = await users.FindByUsernameAsync(command.Username, cancellationToken);

        if (user is null)
        {
            await AuditFailureAsync(null, command.Username, "unknown_user", cancellationToken);
            return Result.Failure<AuthTokens>(IdentityErrors.InvalidCredentials);
        }

        if (!user.IsActive)
        {
            await AuditFailureAsync(user.Id, command.Username, "inactive", cancellationToken);
            return Result.Failure<AuthTokens>(IdentityErrors.InvalidCredentials);
        }

        if (user.IsLockedOut(now))
        {
            await AuditFailureAsync(user.Id, command.Username, "locked_out", cancellationToken);
            return Result.Failure<AuthTokens>(IdentityErrors.AccountLocked);
        }

        if (user.PasswordHash is null || !passwordHasher.Verify(command.Password, user.PasswordHash))
        {
            user.RegisterFailedLogin(settings.MaxFailedAccessAttempts, settings.LockoutDuration, now);
            await AuditFailureAsync(user.Id, command.Username, "bad_password", cancellationToken);
            return Result.Failure<AuthTokens>(IdentityErrors.InvalidCredentials);
        }

        user.RegisterSuccessfulLogin(now);
        var tokens = IssueTokens(user, now, settings);

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.Login,
                UserId = user.Id,
                EntityType = "User",
                EntityId = user.Id.Value,
            },
            cancellationToken);

        return Result.Success(tokens);
    }

    private AuthTokens IssueTokens(User user, DateTimeOffset now, IdentityModuleOptions settings)
    {
        var refresh = tokenGenerator.Create();
        var session = UserSession.Start(
            user.Id,
            refresh.Hash,
            now.Add(settings.RefreshTokenLifetime),
            currentUser.IpAddress,
            currentUser.UserAgent,
            now);

        sessions.Add(session);
        var access = tokenIssuer.Issue(user, session.Id);

        return new AuthTokens(
            access.Value,
            access.ExpiresAt,
            refresh.Value,
            session.ExpiresAt,
            new AuthenticatedUserDto(
                user.Id.Value,
                user.Username,
                user.DisplayName,
                user.IsSystemAdmin,
                user.MustChangePassword));
    }

    private Task AuditFailureAsync(UserId? userId, string username, string reason, CancellationToken cancellationToken) =>
        audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.LoginFailed,
                Outcome = AuditOutcome.Denied,
                ActorType = userId is null ? AuditActorType.Anonymous : AuditActorType.User,
                UserId = userId,
                EntityType = "User",
                EntityId = userId?.Value,
                Metadata = new Dictionary<string, object?>
                {
                    ["username"] = username,
                    ["reason"] = reason,
                },
            },
            cancellationToken);
}

public sealed class RefreshTokenHandler(
    IUserRepository users,
    IUserSessionRepository sessions,
    IAccessTokenIssuer tokenIssuer,
    ISecureTokenGenerator tokenGenerator,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IOptions<IdentityModuleOptions> options,
    TimeProvider timeProvider) : ICommandHandler<RefreshTokenCommand, Result<AuthTokens>>
{
    public async Task<Result<AuthTokens>> HandleAsync(RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var settings = options.Value;
        var hash = tokenGenerator.ComputeHash(command.RefreshToken);
        var session = await sessions.FindByTokenHashAsync(hash, cancellationToken);

        if (session is null)
        {
            return Result.Failure<AuthTokens>(IdentityErrors.InvalidRefreshToken);
        }

        if (!session.IsActive(now))
        {
            // A revoked token being replayed means it leaked: end every session of that user.
            if (session.RevokedAt is not null)
            {
                foreach (var active in await sessions.ListActiveAsync(session.UserId, cancellationToken))
                {
                    active.Revoke(now);
                }

                await audit.WriteAsync(
                    new AuditRecord
                    {
                        Action = AuditActions.TokenReuseDetected,
                        Outcome = AuditOutcome.Denied,
                        UserId = session.UserId,
                        EntityType = "UserSession",
                        EntityId = session.Id.Value,
                    },
                    cancellationToken);
            }

            return Result.Failure<AuthTokens>(IdentityErrors.InvalidRefreshToken);
        }

        var user = await users.FindAsync(session.UserId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            session.Revoke(now);
            return Result.Failure<AuthTokens>(IdentityErrors.InvalidRefreshToken);
        }

        var refresh = tokenGenerator.Create();
        var rotated = UserSession.Start(
            user.Id,
            refresh.Hash,
            now.Add(settings.RefreshTokenLifetime),
            currentUser.IpAddress,
            currentUser.UserAgent,
            now);

        sessions.Add(rotated);
        session.Revoke(now, rotated.Id);

        var access = tokenIssuer.Issue(user, rotated.Id);
        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.TokenRefreshed,
                UserId = user.Id,
                EntityType = "UserSession",
                EntityId = rotated.Id.Value,
            },
            cancellationToken);

        return Result.Success(new AuthTokens(
            access.Value,
            access.ExpiresAt,
            refresh.Value,
            rotated.ExpiresAt,
            new AuthenticatedUserDto(
                user.Id.Value,
                user.Username,
                user.DisplayName,
                user.IsSystemAdmin,
                user.MustChangePassword)));
    }
}

public sealed class LogoutHandler(
    IUserSessionRepository sessions,
    ISecureTokenGenerator tokenGenerator,
    IAuditWriter audit,
    TimeProvider timeProvider) : ICommandHandler<LogoutCommand, Result>
{
    public async Task<Result> HandleAsync(LogoutCommand command, CancellationToken cancellationToken)
    {
        var session = await sessions.FindByTokenHashAsync(
            tokenGenerator.ComputeHash(command.RefreshToken),
            cancellationToken);

        if (session is null)
        {
            return Result.Success();
        }

        session.Revoke(timeProvider.GetUtcNow());
        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.Logout,
                UserId = session.UserId,
                EntityType = "UserSession",
                EntityId = session.Id.Value,
            },
            cancellationToken);

        return Result.Success();
    }
}

public sealed class ChangeOwnPasswordHandler(
    IUserRepository users,
    IUserSessionRepository sessions,
    IPasswordHasher passwordHasher,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IOptions<IdentityModuleOptions> options,
    TimeProvider timeProvider) : ICommandHandler<ChangeOwnPasswordCommand, Result>
{
    public async Task<Result> HandleAsync(ChangeOwnPasswordCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure(IdentityErrors.Unauthenticated);
        }

        var user = await users.FindAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure(IdentityErrors.UserNotFound);
        }

        if (user.PasswordHash is null || !passwordHasher.Verify(command.CurrentPassword, user.PasswordHash))
        {
            return Result.Failure(IdentityErrors.InvalidCredentials);
        }

        if (command.NewPassword.Length < options.Value.MinimumPasswordLength)
        {
            return Result.Failure(Error.Validation(
                "password.too_short",
                $"The password must be at least {options.Value.MinimumPasswordLength} characters long."));
        }

        var now = timeProvider.GetUtcNow();
        user.SetPassword(passwordHasher.Hash(command.NewPassword), false, now);

        // Changing a password ends every other session.
        foreach (var session in await sessions.ListActiveAsync(userId, cancellationToken))
        {
            session.Revoke(now);
        }

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.UserPasswordChanged,
                EntityType = "User",
                EntityId = userId.Value,
            },
            cancellationToken);

        return Result.Success();
    }
}
