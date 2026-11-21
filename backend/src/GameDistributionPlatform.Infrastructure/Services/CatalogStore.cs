using GameDistributionPlatform.Application.Exceptions;
using GameDistributionPlatform.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GameDistributionPlatform.Infrastructure.Services;

internal static class CatalogStore
{
    public static async Task SaveAsync(AppDbContext db, string duplicateSlugMessage, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("The record changed during this operation. Reload and try again.");
        }
        catch (DbUpdateException error) when (IsSqlError(error, 2601, 2627))
        {
            throw new ConflictException(duplicateSlugMessage);
        }
        catch (DbUpdateException error) when (IsSqlError(error, 547))
        {
            throw new ConflictException("A related record changed during this operation. Reload and try again.");
        }
        catch (Exception error) when (IsSqlError(error, 1205))
        {
            throw new ConflictException("The catalog changed during this operation. Reload and try again.");
        }
    }

    public static bool IsSqlError(Exception error, params int[] numbers)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlError && numbers.Contains(sqlError.Number))
            {
                return true;
            }
        }

        return false;
    }
}
