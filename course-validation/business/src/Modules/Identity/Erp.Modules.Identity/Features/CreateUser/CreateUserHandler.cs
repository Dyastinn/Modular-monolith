using Erp.Modules.Identity.Domain;
using Erp.Modules.Identity.Infrastructure;
using FluentValidation;
using Mediator;
using Microsoft.AspNetCore.Identity;

namespace Erp.Modules.Identity.Features;

internal sealed record CreateUserCommand(string Email, string FullName, string Role, string TemporaryPassword) : ICommand<long>;

internal sealed class CreateUserValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Role).NotEmpty();
        RuleFor(x => x.TemporaryPassword).NotEmpty();
    }
}

// Staff accounts are made by an admin, with a temporary password the person must change at first sign-in.
internal sealed class CreateUserHandler(UserManager<AppUser> users) : ICommandHandler<CreateUserCommand, long>
{
    public async ValueTask<long> Handle(CreateUserCommand cmd, CancellationToken ct)
    {
        StaffAccountRules.EnsureRoleExists(cmd.Role);

        var email = cmd.Email.Trim();
        var user = new AppUser
        {
            UserName = email, Email = email, EmailConfirmed = true, FullName = cmd.FullName.Trim(), MustChangePassword = true
        };
        (await users.CreateAsync(user, cmd.TemporaryPassword)).ThrowIfFailed();
        (await users.AddToRoleAsync(user, cmd.Role)).ThrowIfFailed();
        return user.Id;
    }
}
