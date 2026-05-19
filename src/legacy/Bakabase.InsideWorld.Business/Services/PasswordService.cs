using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.InsideWorld.Models.Constants.Aos;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Components.Orm.Infrastructures;
using Bootstrap.Models.ResponseModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Services
{
    public class PasswordService : ResourceService<BakabaseDbContext, PasswordDbModel, string>
    {
        public PasswordService(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        public async Task<SearchResponse<PasswordDbModel>> Search(PasswordSearchRequestModel model)
        {
            Expression<Func<PasswordDbModel, object>>? orderBy = null;
            if (model.Order.HasValue)
            {
                switch (model.Order.Value)
                {
                    case PasswordSearchOrder.Latest:
                        orderBy = p => p.LastUsedAt;
                        break;
                    case PasswordSearchOrder.Frequency:
                        orderBy = p => p.UsedTimes;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            return await base.Search(p => true, model.PageIndex, model.PageSize, orderBy);
        }

        public async Task AddUsedTimes(string password)
        {
            // Two decompression tasks (or two retries of the same one) sharing
            // a password race on the same (non-existing) row: both branches
            // see `p == null`, both Add() it, second SaveChangesAsync fails
            // with "UNIQUE constraint failed: Passwords.Text". On conflict,
            // reload the row and keep going — it just means somebody else
            // beat us to the insert.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    var p = await GetByKey(password);
                    if (p == null)
                    {
                        p = new PasswordDbModel {Text = password};
                        DbContext.Add(p);
                    }

                    p.LastUsedAt = DateTime.Now;
                    p.UsedTimes++;
                    await DbContext.SaveChangesAsync();
                    return;
                }
                catch (DbUpdateException ex)
                    when (attempt == 0 && IsUniqueConstraintViolation(ex))
                {
                    // Detach the conflicting tracked entity and retry — the
                    // second pass will load the row that won the race.
                    foreach (var entry in DbContext.ChangeTracker.Entries<PasswordDbModel>().ToList())
                    {
                        entry.State = EntityState.Detached;
                    }
                }
                catch (Exception e)
                {
                    Logger.LogError(e, $"An error occurred adding used times for password: {password}");
                    return;
                }
            }
        }

        private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
            ex.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 };
    }
}