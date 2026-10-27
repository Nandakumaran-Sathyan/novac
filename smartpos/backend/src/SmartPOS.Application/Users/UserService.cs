using Microsoft.EntityFrameworkCore;
using SmartPOS.Application.Auth;
using SmartPOS.Application.Common;
using SmartPOS.Core.Entities;

namespace SmartPOS.Application.Users;

public sealed class UserService(
    IApplicationDbContext db,
    IPasswordHasher passwordHasher,
    ICurrentUser currentUser,
    StoreCalendar calendar) : IUserService
{
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public static UserDto ToDto(User u) =>
        new(u.Id, u.FullName, u.Email, u.Role, u.IsActive, u.CreatedAt, u.LastLoginAt);

    public async Task<PagedResult<UserDto>> ListAsync(UserQuery query, CancellationToken cancellationToken)
    {
        var users = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = SearchPattern.Contains(query.Search);
            users = users.Where(u =>
                EF.Functions.Like(u.FullName, pattern, SearchPattern.EscapeCharacter) ||
                EF.Functions.Like(u.Email, pattern, SearchPattern.EscapeCharacter));
        }
        if (query.Role is { } role)
            users = users.Where(u => u.Role == role);

        return await users
            .OrderBy(u => u.FullName)
            .Select(u => new UserDto(u.Id, u.FullName, u.Email, u.Role, u.IsActive, u.CreatedAt, u.LastLoginAt))
            .ToPagedResultAsync(query, cancellationToken);
    }

    public async Task<UserDto> GetAsync(int id, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, cancellationToken)
                   ?? throw NotFoundException.For("User", id);
        return ToDto(user);
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        if (await db.Users.AnyAsync(u => u.Email == email, cancellationToken))
            throw new ConflictException("A user with this email already exists.");

        var now = calendar.UtcNow;
        var user = new User
        {
            FullName = request.FullName.Trim(),
            Email = email,
            PasswordHash = passwordHasher.Hash(request.Password),
            Role = request.Role,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(user);
    }

    public async Task<UserDto> UpdateAsync(int id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, cancellationToken)
                   ?? throw NotFoundException.For("User", id);

        // Stop an admin locking themselves (and possibly everyone) out.
        if (user.Id == currentUser.UserId && (!request.IsActive || request.Role != user.Role))
            throw new BusinessRuleException("You cannot change your own role or deactivate your own account.");

        user.FullName = request.FullName.Trim();
        user.Role = request.Role;
        user.IsActive = request.IsActive;
        if (!string.IsNullOrEmpty(request.NewPassword))
            user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        user.UpdatedAt = calendar.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return ToDto(user);
    }
}
