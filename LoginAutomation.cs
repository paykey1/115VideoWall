using System.Runtime.InteropServices;
using System.Text.Json;

namespace VideoWall115;

internal enum LoginOutcome
{
    Unavailable, NeedsVerification, Submitted, ManualInput,
}

internal static class LoginAutomation
{
    internal static async Task<LoginOutcome> TryAsync(
        BrowserPane pane, SitePolicy site, LoginDetails details, CancellationToken cancellation = default)
    {
        var prepare = BuildScript(site, """
            if (verification()) return 'verify';
            const form = document.getElementById('js-login_form');
            if (!visible(form)) {
              const link = document.querySelector('a[lgb-nav="login"]');
              if (visible(link) && !link.dataset.wallOpenedLogin) {
                link.dataset.wallOpenedLogin = '1';
                link.click();
              }
              return 'waiting';
            }
            const password = form.querySelector('#js-passwd[type="password"]');
            if (!visible(form.querySelector('#js-account[name="account"]')) || !visible(password) ||
                !visible(form.querySelector('#js-submit'))) return 'waiting';
            if (password.value) return 'manual';
            const agreement = document.querySelector('[rel="login_agreement"] [role="checkbox"]');
            if (!visible(agreement) || !agreement.parentElement.textContent.includes('我已阅读并同意'))
              return 'waiting';
            if (agreement.getAttribute('aria-checked') === 'false' && !agreement.dataset.wallAgreed) {
              agreement.dataset.wallAgreed = '1';
              agreement.click();
            }
            return agreement.getAttribute('aria-checked') === 'true' ? 'ready' : 'waiting';
            """);
        try
        {
            for (var attempt = 0; attempt < 48 && !cancellation.IsCancellationRequested && CanContinue(pane, site); attempt++)
            {
                var state = await ReadAsync(pane, prepare);
                if (state == "verify")
                    return LoginOutcome.NeedsVerification;
                if (state == "manual")
                    return LoginOutcome.ManualInput;
                if (state == "ready" && !cancellation.IsCancellationRequested && CanContinue(pane, site))
                {
                    var signIn = BuildScript(site, $$"""
                        if (verification()) return 'verify';
                        const form = document.getElementById('js-login_form');
                        const account = form?.querySelector('#js-account[name="account"]');
                        const password = form?.querySelector('#js-passwd[type="password"]');
                        const submit = form?.querySelector('#js-submit');
                        const agreement = document.querySelector('[rel="login_agreement"] [role="checkbox"]');
                        if (!visible(form) || !{{JsonSerializer.Serialize(site.SignInOrigins)}}.includes(new URL(form.action).origin) ||
                            !visible(account) || !visible(password) || !visible(submit) ||
                            !visible(agreement) || agreement.getAttribute('aria-checked') !== 'true' ||
                            !agreement.parentElement.textContent.includes('我已阅读并同意') ||
                            submit.disabled || submit.getAttribute('aria-disabled') === 'true') return 'waiting';
                        if (form.dataset.wallSubmitted || password.value ||
                            (account.value && account.value !== {{JsonSerializer.Serialize(details.Account)}}))
                          return 'manual';
                        form.dataset.wallSubmitted = '1';
                        const fill = (field, value) => {
                          field.value = value;
                          field.dispatchEvent(new Event('input', { bubbles: true }));
                          field.dispatchEvent(new Event('change', { bubbles: true }));
                        };
                        fill(account, {{JsonSerializer.Serialize(details.Account)}});
                        fill(password, {{JsonSerializer.Serialize(details.Password)}});
                        submit.click();
                        return 'submitted';
                        """);
                    var result = await ReadAsync(pane, signIn);
                    if (result == "verify")
                        return LoginOutcome.NeedsVerification;
                    if (result == "manual")
                        return LoginOutcome.ManualInput;
                    if (result == "submitted")
                        return await WaitForVerificationAsync(pane, site, cancellation);
                }
                await Task.Delay(250, cancellation);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is InvalidOperationException or COMException)
        {
            // Navigation can dispose the original page while a probe is in flight.
        }
        return LoginOutcome.Unavailable;
    }

    private static async Task<LoginOutcome> WaitForVerificationAsync(BrowserPane pane, SitePolicy site,
        CancellationToken cancellation)
    {
        var check = BuildScript(site, "return verification() ? 'verify' : 'waiting';");
        for (var attempt = 0; attempt < 28 && !cancellation.IsCancellationRequested && CanContinue(pane, site); attempt++)
        {
            if (await ReadAsync(pane, check) == "verify")
                return LoginOutcome.NeedsVerification;
            await Task.Delay(250, cancellation);
        }
        return LoginOutcome.Submitted;
    }

    private static bool CanContinue(BrowserPane pane, SitePolicy site) =>
        !pane.IsDisposed && pane.View.CoreWebView2 != null && site.IsOfficialSignInPage(pane.Core.Source);

    private static async Task<string?> ReadAsync(BrowserPane pane, string script) =>
        JsonSerializer.Deserialize<string>(await pane.Core.ExecuteScriptAsync(script));

    private static string BuildScript(SitePolicy site, string body) => $$"""
        (() => {
          if (!{{JsonSerializer.Serialize(site.SignInOrigins)}}.includes(location.origin)) return 'waiting';
          const visible = element => Boolean(element && element.getClientRects().length &&
            getComputedStyle(element).visibility !== 'hidden');
          const verification = () => [...document.querySelectorAll(
            '#js-mvcode, #js-bind_mobile-box, input[autocomplete="one-time-code"], iframe[src*="captcha" i]')]
            .some(visible);
          {{body}}
        })()
        """;
}
