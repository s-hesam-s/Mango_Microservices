using Microsoft.AspNetCore.Mvc;
namespace Mango.Web.Configuration;

public static class MvcConfiguration
{
    public static IMvcBuilder AddSecureMvc(this IServiceCollection services) =>
        services.AddControllersWithViews(options =>
        {
            options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());

        });
}
