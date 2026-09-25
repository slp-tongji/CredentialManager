using System.Security.Claims;
using Tjslp.CredentialManager.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Tjslp.CredentialManager.Pages;

[Authorize]
public sealed class IndexModel : PageModel
{
    private readonly AppOptions appOptions;
    private readonly CredentialService credentialService;

    public string Owner => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public string UserName => User.FindFirstValue(ClaimTypes.Name) ?? Owner;

    public string Title => appOptions.Title;

    public IReadOnlyList<(string CredentialId, string Alias, string Owner, DateTimeOffset? Expire)> Credentials { get; private set; }
        = [];
    public (string CredentialId, string Credential, DateTimeOffset? Expire)? NewCredential { get; private set; }
    public string? Error { get; private set; }

    public IndexModel(AppOptions appOptions, CredentialService credentialService)
    {
        this.appOptions = appOptions;
        this.credentialService = credentialService;
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(string alias, string? expire, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            await LoadAsync(cancellationToken);
            Error = "名称不能为空。";
            return Page();
        }

        DateTimeOffset? parsedExpire = null;
        if (!string.IsNullOrWhiteSpace(expire))
        {
            if (!DateTimeOffset.TryParse(expire, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
            {
                await LoadAsync(cancellationToken);
                Error = "过期时间格式无法解析，请使用 ISO 格式（如 2026-10-01T00:00:00Z）。";
                return Page();
            }
            parsedExpire = parsed;
        }

        NewCredential = await credentialService.CreateAsync(Owner, alias.Trim(), parsedExpire, cancellationToken);

        if (NewCredential is null)
        {
            Error = "创建失败：下游未返回有效响应。";
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostRevokeAsync(string id, CancellationToken cancellationToken)
    {
        var revoked = await credentialService.RevokeOwnAsync(id, Owner, cancellationToken);
        if (!revoked)
        {
            Error = "吊销失败：没有权限，或下游已拒绝该操作。";
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var result = new List<(string CredentialId, string Alias, string Owner, DateTimeOffset? Expire)>();
        await foreach (var credential in credentialService.ListOwnAsync(Owner, cancellationToken))
        {
            result.Add(credential);
        }
        Credentials = result;
    }
}
