using Microsoft.EntityFrameworkCore;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Models;

namespace Zitbaba.Web.Services;

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryRow>> ListAsync();
    Task<CategoryFormModel?> GetFormAsync(int id);
    Task<IReadOnlyList<CategoryOption>> GetParentOptionsAsync(int? excludeId);
    Task<CategoryResult> CreateAsync(Guid actorId, string name, int? parentId, int sortOrder);
    Task<CategoryResult> UpdateAsync(Guid actorId, int id, string name, int? parentId, int sortOrder);
    Task<CategoryResult> SetActiveAsync(Guid actorId, int id, bool active);
    Task<CategoryResult> DeleteAsync(Guid actorId, int id);
}

public class CategoryService : ICategoryService
{
    private const string DuplicateMessage = "A category with that name already exists in this location.";

    private readonly ZitbabaDbContext _db;
    private readonly IAuditService _audit;

    public CategoryService(ZitbabaDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<CategoryRow>> ListAsync()
    {
        var all = await _db.Categories.AsNoTracking()
            .Select(c => new
            {
                c.CategoryId,
                c.Name,
                c.ParentCategoryId,
                c.IsActive,
                c.SortOrder,
                ProductCount = c.Products.Count(),
                ChildCount = c.Children.Count()
            })
            .ToListAsync();

        var rows = new List<CategoryRow>();
        foreach (var top in all.Where(c => c.ParentCategoryId == null)
                     .OrderBy(c => c.SortOrder).ThenBy(c => c.Name))
        {
            rows.Add(new CategoryRow(top.CategoryId, top.Name, null, top.IsActive, top.SortOrder, top.ProductCount, top.ChildCount));
            foreach (var child in all.Where(c => c.ParentCategoryId == top.CategoryId)
                         .OrderBy(c => c.SortOrder).ThenBy(c => c.Name))
            {
                rows.Add(new CategoryRow(child.CategoryId, child.Name, top.Name, child.IsActive, child.SortOrder, child.ProductCount, child.ChildCount));
            }
        }
        return rows;
    }

    public async Task<CategoryFormModel?> GetFormAsync(int id)
    {
        return await _db.Categories.AsNoTracking()
            .Where(c => c.CategoryId == id)
            .Select(c => new CategoryFormModel
            {
                CategoryId = c.CategoryId,
                Name = c.Name,
                ParentCategoryId = c.ParentCategoryId,
                SortOrder = c.SortOrder
            })
            .FirstOrDefaultAsync();
    }

    public async Task<IReadOnlyList<CategoryOption>> GetParentOptionsAsync(int? excludeId)
    {
        var query = _db.Categories.AsNoTracking().Where(c => c.ParentCategoryId == null);
        if (excludeId.HasValue)
        {
            var id = excludeId.Value;
            query = query.Where(c => c.CategoryId != id);
        }

        return await query
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new CategoryOption(c.CategoryId, c.Name, c.IsActive))
            .ToListAsync();
    }

    public async Task<CategoryResult> CreateAsync(Guid actorId, string name, int? parentId, int sortOrder)
    {
        name = (name ?? string.Empty).Trim();
        var error = await ValidateAsync(null, name, parentId);
        if (error is not null) return CategoryResult.Fail(error);

        // Transaction: the audit row needs the generated id, so we save twice but commit once.
        await using var tx = await _db.Database.BeginTransactionAsync();

        var category = new Category
        {
            Name = name,
            ParentCategoryId = parentId,
            SortOrder = sortOrder,
            IsActive = true
        };
        _db.Categories.Add(category);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return CategoryResult.Fail(DuplicateMessage);
        }

        _audit.Stage(actorId, "CategoryCreated", nameof(Category), category.CategoryId.ToString(),
            new { categoryId = category.CategoryId, name, parentId, sortOrder });
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return CategoryResult.Ok(category.CategoryId);
    }

    public async Task<CategoryResult> UpdateAsync(Guid actorId, int id, string name, int? parentId, int sortOrder)
    {
        name = (name ?? string.Empty).Trim();

        var category = await _db.Categories.FirstOrDefaultAsync(c => c.CategoryId == id);
        if (category is null) return CategoryResult.Fail("Category not found.");

        var error = await ValidateAsync(id, name, parentId);
        if (error is not null) return CategoryResult.Fail(error);

        category.Name = name;
        category.ParentCategoryId = parentId;
        category.SortOrder = sortOrder;

        _audit.Stage(actorId, "CategoryUpdated", nameof(Category), id.ToString(),
            new { categoryId = id, name, parentId, sortOrder });

        return await SaveAsync(id, DuplicateMessage);
    }

    public async Task<CategoryResult> SetActiveAsync(Guid actorId, int id, bool active)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.CategoryId == id);
        if (category is null) return CategoryResult.Fail("Category not found.");
        if (category.IsActive == active) return CategoryResult.Ok(id);

        category.IsActive = active;
        _audit.Stage(actorId, active ? "CategoryActivated" : "CategoryDeactivated",
            nameof(Category), id.ToString(), new { categoryId = id, name = category.Name });

        return await SaveAsync(id, "Could not update this category.");
    }

    public async Task<CategoryResult> DeleteAsync(Guid actorId, int id)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.CategoryId == id);
        if (category is null) return CategoryResult.Fail("Category not found.");

        if (await _db.Products.AnyAsync(p => p.CategoryId == id))
            return CategoryResult.Fail("This category has products, so it cannot be deleted. Deactivate it instead.");

        if (await _db.Categories.AnyAsync(c => c.ParentCategoryId == id))
            return CategoryResult.Fail("This category has subcategories, so it cannot be deleted. Deactivate it instead.");

        _db.Categories.Remove(category);
        _audit.Stage(actorId, "CategoryDeleted", nameof(Category), id.ToString(),
            new { categoryId = id, name = category.Name });

        return await SaveAsync(id, "This category is in use and cannot be deleted. Deactivate it instead.");
    }

    private async Task<CategoryResult> SaveAsync(int id, string failureMessage)
    {
        try
        {
            await _db.SaveChangesAsync();
            return CategoryResult.Ok(id);
        }
        catch (DbUpdateException)
        {
            return CategoryResult.Fail(failureMessage);
        }
    }

    private async Task<string?> ValidateAsync(int? editingId, string name, int? parentId)
    {
        if (name.Length < 2 || name.Length > 100)
            return "Name must be between 2 and 100 characters.";

        if (parentId is int pid)
        {
            if (editingId == pid) return "A category cannot be its own parent.";

            var parent = await _db.Categories.AsNoTracking()
                .Where(c => c.CategoryId == pid)
                .Select(c => new { c.ParentCategoryId })
                .FirstOrDefaultAsync();

            if (parent is null) return "The selected parent category does not exist.";
            if (parent.ParentCategoryId is not null) return "Only one level of subcategory is allowed.";

            if (editingId is int eid && await _db.Categories.AnyAsync(c => c.ParentCategoryId == eid))
                return "This category has subcategories, so it cannot become a subcategory itself.";
        }

        var lowered = name.ToLower();
        var duplicate = await _db.Categories.AnyAsync(c =>
            c.ParentCategoryId == parentId
            && c.Name.ToLower() == lowered
            && (editingId == null || c.CategoryId != editingId));

        return duplicate ? DuplicateMessage : null;
    }
}
