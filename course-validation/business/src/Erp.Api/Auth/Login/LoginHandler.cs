using System.Security.Authentication;
using Erp.Shared.Infrastructure;
using Erp.Shared.Messaging;
using FluentValidation;

namespace Erp.Api.Auth;

public sealed record LoginCommand(string Username, string Password) : ICommand<LoginResult>;
public sealed record LoginResult(string Token, string Username, string Role);

public sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.Username).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class LoginHandler(
    IUserRepository users,
    IPasswordHasher hasher,
    IJwtTokenService tokens) : ICommandHandler<LoginCommand, LoginResult>
{
    public async Task<LoginResult> Handle(LoginCommand cmd, CancellationToken ct)
    {
        var user = await users.GetByUsernameAsync(cmd.Username, ct);

        // Same generic error whether the user is missing OR the password is wrong.
        if (user is null || !user.IsActive || !hasher.Verify(cmd.Password, user.PasswordHash))
            throw new AuthenticationException("Invalid username or password.");

        var token = tokens.CreateToken(user.Id, user.Username, user.Role);
        return new LoginResult(token, user.Username, user.Role);
    }
}
