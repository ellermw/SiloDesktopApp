using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Models.Catalog;
namespace SiloPlayer.Views;

public sealed partial class ItemDetailPage
{
    /// <summary>Shows the viewer's rating, a score from 1 to 10, in the capsule: five stars with half stars.</summary>
    private void UpdateStarRating() => StarRatingContainer.Value = ViewModel.UserRating;

    /// <summary>
    /// Applies what the viewer picked in the capsule. The rating command toggles when handed the score already
    /// set, so clearing hands it the current score, and a pick that changes nothing hands it nothing.
    /// </summary>
    private async void StarRating_ScoreChosen(int? picked)
    {
        if (RatingScale.CommandArgument(ViewModel.UserRating, picked) is not { } command) { UpdateStarRating(); return; }
        await ViewModel.SetRatingCommand.ExecuteAsync(command);
        UpdateStarRating();
    }

    /// <summary>The narrow layout's Rate submenu: one entry per half star, each setting its own score.</summary>
    private MenuFlyoutSubItem BuildRateSubmenu()
    {
        var rate = SiloPlayer.Controls.MediaItemCardActions.Localize(new MenuFlyoutSubItem { Text = "Rate" }, text: "web.item.rating.label");
        for (var score = RatingScale.MinScore; score <= RatingScale.MaxScore; score++)
        {
            var value = score;
            var star = SiloPlayer.Controls.MediaItemCardActions.BindText(new MenuFlyoutItem(), "Text", element => element.Text = RatingScale.Label(value));
            star.Click += async (_, _) =>
            {
                await ViewModel.SetRatingCommand.ExecuteAsync(value);
                UpdateStarRating(); BuildMoreFlyout();
            };
            rate.Items.Add(star);
        }
        return rate;
    }
}
