using System.IO;
using System.Windows.Controls;
using OpenQuickHost;

internal static class MobileMessageComposerVerification
{
    public static async Task RunAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var isolatedRoot = Path.Combine(Path.GetTempPath(), "yanzi-multiline-composer-" + Guid.NewGuid().ToString("N"));
            try
            {
                using var isolation = HostAssets.UseIsolatedDataRootForVerification(isolatedRoot);
                var app = new App { IsVerificationHarness = true };
                app.InitializeComponent();
                var window = new MobileMessageToastWindow();
                var input = (TextBox)window.FindName("InputTextBox");
                if (!input.AcceptsReturn || input.TextWrapping != System.Windows.TextWrapping.Wrap ||
                    input.MinLines != 1 || input.MaxLines != 5 ||
                    input.VerticalScrollBarVisibility != ScrollBarVisibility.Auto)
                    throw new InvalidOperationException("Mobile message composer must accept and wrap up to five visible lines.");

                const string pastedText = "第一行\r\n第二行\n第三行";
                input.Text = pastedText;
                if (!string.Equals(input.Text, pastedText, StringComparison.Ordinal))
                    throw new InvalidOperationException("Pasted message line breaks were lost.");
                if (input.Height is > 0 and < 60)
                    throw new InvalidOperationException("The composer must not have a fixed single-line height.");

                window.Close();
                app.Shutdown();
                Console.WriteLine("Mobile message multiline composer verification PASSED: accepts lines, wraps text, preserves CR/LF, no fixed height.");
                completion.SetResult();
            }
            catch (Exception error) { completion.SetException(error); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task;
    }
}
