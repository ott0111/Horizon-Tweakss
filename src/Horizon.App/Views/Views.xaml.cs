using System.Windows.Controls;

namespace Horizon.App.Views;

public partial class SignInView : UserControl
{
    public SignInView() => InitializeComponent();
    private void Password_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not PasswordBox password) return;
        PasswordWatermark.Visibility = string.IsNullOrEmpty(password.Password) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        if (DataContext is ViewModels.SignInViewModel viewModel) viewModel.Password = password.Password;
    }
}
public partial class OnboardingView : UserControl { public OnboardingView() => InitializeComponent(); }
public partial class SignUpView : UserControl
{
    public SignUpView() => InitializeComponent();
    private void SignUpPassword_Changed(object sender, System.Windows.RoutedEventArgs e) { if (DataContext is ViewModels.SignUpViewModel vm && sender is PasswordBox box) vm.Password = box.Password; }
    private void SignUpConfirmPassword_Changed(object sender, System.Windows.RoutedEventArgs e) { if (DataContext is ViewModels.SignUpViewModel vm && sender is PasswordBox box) vm.ConfirmPassword = box.Password; }
}
public partial class ForgotPasswordView : UserControl { public ForgotPasswordView() => InitializeComponent(); }
public partial class ResetPasswordView : UserControl
{
    public ResetPasswordView() => InitializeComponent();
    private void ResetPassword_Changed(object sender, System.Windows.RoutedEventArgs e) { if (DataContext is ViewModels.ResetPasswordViewModel vm && sender is PasswordBox box) vm.Password = box.Password; }
    private void ResetConfirmPassword_Changed(object sender, System.Windows.RoutedEventArgs e) { if (DataContext is ViewModels.ResetPasswordViewModel vm && sender is PasswordBox box) vm.ConfirmPassword = box.Password; }
}
public partial class ShellView : UserControl { public ShellView() => InitializeComponent(); }
public partial class DashboardView : UserControl { public DashboardView() => InitializeComponent(); }
public partial class RecommendationsView : UserControl { public RecommendationsView() => InitializeComponent(); }
public partial class TweaksView : UserControl { public TweaksView() => InitializeComponent(); }
public partial class FortniteView : UserControl { public FortniteView() => InitializeComponent(); }
public partial class DebloatView : UserControl { public DebloatView() => InitializeComponent(); }
public partial class StartupView : UserControl { public StartupView() => InitializeComponent(); }
public partial class CleanupView : UserControl { public CleanupView() => InitializeComponent(); }
public partial class SystemView : UserControl { public SystemView() => InitializeComponent(); }
public partial class CompatibilityView : UserControl { public CompatibilityView() => InitializeComponent(); }
public partial class BenchmarkView : UserControl { public BenchmarkView() => InitializeComponent(); }
public partial class RestoreView : UserControl { public RestoreView() => InitializeComponent(); }
public partial class HistoryView : UserControl { public HistoryView() => InitializeComponent(); }
public partial class PlanView : UserControl { public PlanView() => InitializeComponent(); }
public partial class SettingsView : UserControl { public SettingsView() => InitializeComponent(); }
public partial class SupportView : UserControl { public SupportView() => InitializeComponent(); }
public partial class ProfileView : UserControl { public ProfileView() => InitializeComponent(); }
