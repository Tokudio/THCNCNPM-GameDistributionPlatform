using System.Data;
using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Application.Exceptions;
using GameDistributionPlatform.Application.Interfaces;
using GameDistributionPlatform.Infrastructure.Data;
using GameDistributionPlatform.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GameDistributionPlatform.Infrastructure.Services;

public sealed class AdminUserService(AppDbContext db, UserManager<ApplicationUser> userManager) : IAdminUserService
{
    private const string NormalizedAdmin = "ADMIN";

    public async Task<PageResponse<AdminUserSummaryResponse>> GetUsersAsync(AdminUserQuery query, CancellationToken cancellationToken)
    {
        CatalogInput.Validate(query);
        var users = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            users = users.Where(user => user.DisplayName.ToLower().Contains(search) ||
                (user.Email != null && user.Email.ToLower().Contains(search)));
        }
        if (query.IsActive is { } active) users = users.Where(user => user.IsActive == active);
        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            var role = query.Role.ToUpperInvariant();
            users = users.Where(user => db.UserRoles.Any(link => link.UserId == user.Id &&
                db.Roles.Any(record => record.Id == link.RoleId && record.NormalizedName == role)));
        }

        // Chỉ lấy role của người dùng trong trang đã cắt tại SQL.
        var count = await users.CountAsync(cancellationToken);
        var items = await Summaries(users.OrderByDescending(user => user.CreatedAt).ThenByDescending(user => user.Id)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize)).AsSingleQuery().ToListAsync(cancellationToken);
        return new PageResponse<AdminUserSummaryResponse>(query.PageNumber, query.PageSize, count,
            (int)Math.Ceiling((decimal)count / query.PageSize), items);
    }

    public async Task<AdminUserDetailResponse> GetUserAsync(Guid id, CancellationToken cancellationToken)
    {
        var detail = await Summaries(db.Users.AsNoTracking().Where(user => user.Id == id))
            .Select(user => new AdminUserDetailResponse(user,
                db.Orders.Count(order => order.UserId == id), db.UserLibraries.Count(item => item.UserId == id)))
            .AsSingleQuery().SingleOrDefaultAsync(cancellationToken);
        return detail ?? throw new NotFoundException("Không tìm thấy tài khoản.");
    }

    public async Task UpdateStatusAsync(Guid actorId, Guid id, bool isActive, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            // Khóa chung dòng role Admin cho các lần cập nhật trạng thái. SQLite giữ khóa ghi ngay khi bắt đầu transaction.
            var roles = db.Database.IsSqlServer()
                ? db.Roles.FromSqlInterpolated($"SELECT * FROM [AspNetRoles] WITH (UPDLOCK, HOLDLOCK) WHERE [NormalizedName] = {NormalizedAdmin}")
                : db.Roles.Where(role => role.NormalizedName == NormalizedAdmin);
            var adminRoleId = await roles.Select(role => (Guid?)role.Id).SingleOrDefaultAsync(cancellationToken);
            var activeAdmins = db.Users.AsNoTracking().Where(user => user.IsActive &&
                db.UserRoles.Any(link => link.UserId == user.Id && link.RoleId == adminRoleId));

            // Hai Admin có thể vượt qua middleware cùng lúc. Kiểm quyền lại sau khi lấy khóa, trước khi ghi.
            if (!await activeAdmins.AnyAsync(user => user.Id == actorId, cancellationToken))
                throw new UnauthorizedException("Phiên quản trị không còn hiệu lực.");

            var target = await db.Users.SingleOrDefaultAsync(user => user.Id == id, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy tài khoản.");
            if (target.IsActive == isActive) return;
            if (!isActive && target.Id == actorId)
                throw new ConflictException("Bạn không thể tự tạm khóa tài khoản của mình.");
            if (!isActive && await db.UserRoles.AnyAsync(link => link.UserId == id && link.RoleId == adminRoleId, cancellationToken) &&
                !await activeAdmins.AnyAsync(user => user.Id != id, cancellationToken))
                throw new ConflictException("Hệ thống phải có ít nhất một Admin đang hoạt động.");
            if (target.SecurityStamp is null)
                throw new ConflictException("Không thể lưu trạng thái vì hồ sơ Identity của tài khoản chưa hợp lệ.");

            cancellationToken.ThrowIfCancellationRequested();
            // Identity lưu trạng thái và kiểm tra ConcurrencyStamp. JWT hiện có bị từ chối ở request kế tiếp khi tài khoản bị khóa.
            target.IsActive = isActive;
            var result = await userManager.UpdateAsync(target);
            if (!result.Succeeded) throw new ConflictException("Không thể lưu trạng thái tài khoản. Hãy tải lại và thử lại.");
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception error) when (error is DbUpdateConcurrencyException || error.GetBaseException() is SqlException { Number: 1205 })
        {
            throw new ConflictException("Trạng thái tài khoản vừa thay đổi. Hãy tải lại và thử lại.");
        }
    }

    private IQueryable<AdminUserSummaryResponse> Summaries(IQueryable<ApplicationUser> users) => users.Select(user =>
        new AdminUserSummaryResponse(user.Id, user.DisplayName, user.Email, user.IsActive,
            DateTime.SpecifyKind(user.CreatedAt, DateTimeKind.Utc),
            (from link in db.UserRoles
             join role in db.Roles on link.RoleId equals role.Id
             where link.UserId == user.Id && role.Name != null
             orderby role.Name, role.Id
             select role.Name!).ToArray()));
}
