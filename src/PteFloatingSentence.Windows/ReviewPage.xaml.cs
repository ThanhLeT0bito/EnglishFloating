using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;

namespace PteFloatingSentence.Windows;

public partial class ReviewPage : UserControl
{
    public ReviewPage()
    {
        InitializeComponent();
    }

    public void LoadData(ReviewViewModel viewModel)
    {
        ReviewEmptyStateLabel.Visibility = viewModel.HasData ? Visibility.Collapsed : Visibility.Visible;
        ReviewList.Visibility = viewModel.HasData ? Visibility.Visible : Visibility.Collapsed;
        ReviewList.ItemsSource = viewModel.Summaries;
    }
}
