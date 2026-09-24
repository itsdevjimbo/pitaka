using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace PitakaApp.Api.Actions;

public class CheckUserScopedNameExists
{
    public Task<bool> ExecuteAsync<TEntity>(
        IQueryable<TEntity> resources,
        int userId,
        string name,
        Expression<Func<TEntity, int>> userIdSelector,
        Expression<Func<TEntity, string>> nameSelector,
        Expression<Func<TEntity, int>> idSelector,
        int? excludeId,
        CancellationToken cancellationToken
    )
        where TEntity : class
    {
        var userIdPredicate = Expression.Lambda<Func<TEntity, bool>>(
            Expression.Equal(userIdSelector.Body, Expression.Constant(userId)),
            userIdSelector.Parameters
        );
        return ExecuteAsyncCore(
            resources,
            userIdPredicate,
            name,
            nameSelector,
            idSelector,
            excludeId,
            cancellationToken
        );
    }

    public Task<bool> ExecuteAsync<TEntity>(
        IQueryable<TEntity> resources,
        int userId,
        string name,
        Expression<Func<TEntity, int?>> userIdSelector,
        Expression<Func<TEntity, string>> nameSelector,
        Expression<Func<TEntity, int>> idSelector,
        int? excludeId,
        CancellationToken cancellationToken
    )
        where TEntity : class
    {
        var userIdPredicate = Expression.Lambda<Func<TEntity, bool>>(
            Expression.Equal(userIdSelector.Body, Expression.Constant((int?)userId)),
            userIdSelector.Parameters
        );
        return ExecuteAsyncCore(
            resources,
            userIdPredicate,
            name,
            nameSelector,
            idSelector,
            excludeId,
            cancellationToken
        );
    }

    private static Task<bool> ExecuteAsyncCore<TEntity>(
        IQueryable<TEntity> resources,
        Expression<Func<TEntity, bool>> userIdPredicate,
        string name,
        Expression<Func<TEntity, string>> nameSelector,
        Expression<Func<TEntity, int>> idSelector,
        int? excludeId,
        CancellationToken cancellationToken
    )
        where TEntity : class
    {
        var resource = Expression.Parameter(typeof(TEntity), "resource");
        var resourceUserIdPredicate = ReplaceParameter(userIdPredicate, resource);

        var resourceName = ReplaceParameter(nameSelector, resource);
        var predicate = Expression.AndAlso(
            resourceUserIdPredicate,
            Expression.Equal(resourceName, Expression.Constant(name))
        );

        if (excludeId is int id)
        {
            var resourceId = ReplaceParameter(idSelector, resource);
            predicate = Expression.AndAlso(
                predicate,
                Expression.NotEqual(resourceId, Expression.Constant(id))
            );
        }

        var filter = Expression.Lambda<Func<TEntity, bool>>(predicate, resource);
        return resources.AsNoTracking().AnyAsync(filter, cancellationToken);
    }

    private static Expression ReplaceParameter<TEntity, TProperty>(
        Expression<Func<TEntity, TProperty>> selector,
        ParameterExpression parameter
    ) => new ParameterReplacer(selector.Parameters[0], parameter).Visit(selector.Body)!;

    private sealed class ParameterReplacer(
        ParameterExpression original,
        ParameterExpression replacement
    ) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == original ? replacement : base.VisitParameter(node);
    }
}
