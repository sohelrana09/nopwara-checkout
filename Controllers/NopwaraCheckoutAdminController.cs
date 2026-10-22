using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.NopwaraCheckout.Models;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Misc.NopwaraCheckout.Controllers;

[Area(AreaNames.ADMIN)]
[AuthorizeAdmin]
[AutoValidateAntiforgeryToken]
public class NopwaraCheckoutAdminController : BasePluginController
{
    #region Fields

    private readonly ILocalizationService _localizationService;
    private readonly INotificationService _notificationService;
    private readonly ISettingService _settingService;
    private readonly NopwaraCheckoutSettings _settings;

    #endregion

    #region Ctor

    public NopwaraCheckoutAdminController(ILocalizationService localizationService,
        INotificationService notificationService,
        ISettingService settingService,
        NopwaraCheckoutSettings settings)
    {
        _localizationService = localizationService;
        _notificationService = notificationService;
        _settingService = settingService;
        _settings = settings;
    }

    #endregion

    #region Methods

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public IActionResult Configure()
    {
        var model = new ConfigurationModel
        {
            Enabled = _settings.Enabled,
            AllowedThemes = _settings.AllowedThemes,
            AllowCartEditing = _settings.AllowCartEditing,
            ShowDiscountBox = _settings.ShowDiscountBox,
            ShowGiftCardBox = _settings.ShowGiftCardBox,
            ThemesWithBuiltInStyles = _settings.ThemesWithBuiltInStyles,
            ThemesWithAlpine = _settings.ThemesWithAlpine,
            ShowLoginOnCheckout = _settings.ShowLoginOnCheckout,
            ShowCheckoutAttributes = _settings.ShowCheckoutAttributes,
            SkipShoppingCart = _settings.SkipShoppingCart,
            ShowBuyNowButton = _settings.ShowBuyNowButton
        };

        return View($"~/Plugins/{NopwaraCheckoutDefaults.PLUGIN_FOLDER}/Views/Admin/Configure.cshtml", model);
    }

    [HttpPost, ActionName("Configure")]
    [FormValueRequired("save")]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> Configure(ConfigurationModel model)
    {
        if (!ModelState.IsValid)
            return Configure();

        _settings.Enabled = model.Enabled;
        _settings.AllowedThemes = model.AllowedThemes?.Trim() ?? string.Empty;
        _settings.AllowCartEditing = model.AllowCartEditing;
        _settings.ShowDiscountBox = model.ShowDiscountBox;
        _settings.ShowGiftCardBox = model.ShowGiftCardBox;
        _settings.ThemesWithBuiltInStyles = model.ThemesWithBuiltInStyles?.Trim() ?? string.Empty;
        _settings.ThemesWithAlpine = model.ThemesWithAlpine?.Trim() ?? string.Empty;
        _settings.ShowLoginOnCheckout = model.ShowLoginOnCheckout;
        _settings.ShowCheckoutAttributes = model.ShowCheckoutAttributes;
        _settings.SkipShoppingCart = model.SkipShoppingCart;
        _settings.ShowBuyNowButton = model.ShowBuyNowButton;

        await _settingService.SaveSettingAsync(_settings);

        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Plugins.Saved"));

        return Configure();
    }

    #endregion
}
