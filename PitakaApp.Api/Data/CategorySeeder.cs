namespace PitakaApp.Api.Data;

using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Models;
using PitakaApp.Api.Enums;

public static class CategorySeeder
{
    public static void Seed(DbContext context)
    {
        if (context.Set<Category>().Any())
        {
            return;
        }
        
        var user = SeedHelper.ExtractTransactionUser(context);

        var categories = new List<Category>();

        var systemCategories = new Dictionary<CategoryType, List<string>>
        {
            [CategoryType.Expense] = new List<string>
            {
                "Food & Dining", "Housing", "Transportation", "Health & Wellness",
                "Shopping", "Entertainment", "Bills & Utilities", "Education",
                "Travel", "Family & Pets", "Debt Payments", "Miscellaneous"
            },
            [CategoryType.Income] = new List<string>
            {
                "Salary", "Investments",
                "Gifts Received", "Refunds & Reimbursements", "Other Income"
            }
        };

        foreach (var category in systemCategories)
        {
            foreach (var categoryName in category.Value)
            {
                categories.Add(new Category
                {
                    Name = categoryName,
                    Type = category.Key,
                    IsDefault = true,
                });
            }
        }

        context.Set<Category>().AddRange(categories);


        var userCategory = new Category
        {
            User = user,
            Name = "Freelance Income",
            Type = CategoryType.Income,
            IsDefault = false,
        };

        context.Set<Category>().Add(userCategory);
    }
}