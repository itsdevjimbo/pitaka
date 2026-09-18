using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Data;

public static class CategorySeeder
{
    public static void Seed(DbContext context)
    {
        if (context.Set<Category>().Any())
        {
            return;
        }
        var categories = new List<Category>();

        var systemCategories = new Dictionary<CategoryType, List<string>>
        {
            [CategoryType.Expense] =
            [
                "Food & Dining",
                "Housing",
                "Transportation",
                "Health & Wellness",
                "Shopping",
                "Entertainment",
                "Bills & Utilities",
                "Education",
                "Travel",
                "Family & Pets",
                "Debt Payments",
                "Miscellaneous",
            ],
            [CategoryType.Income] =
            [
                "Salary",
                "Investments",
                "Gifts Received",
                "Refunds & Reimbursements",
                "Other Income",
            ],
        };

        foreach (var category in systemCategories)
        {
            foreach (var categoryName in category.Value)
            {
                categories.Add(
                    new Category
                    {
                        Name = categoryName,
                        Type = category.Key,
                        IsDefault = true,
                    }
                );
            }
        }

        context.Set<Category>().AddRange(categories);
    }
}
