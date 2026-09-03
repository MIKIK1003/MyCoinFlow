using MyCoinFlow.WinUI.Data;
using MyCoinFlow.WinUI.Models;

namespace MyCoinFlow.WinUI.ViewModels;

public sealed class InvoicingViewModel
{
    private readonly InvoicingWorkspaceRepository _repository;

    public InvoicingViewModel(InvoicingWorkspaceRepository? repository = null)
    {
        _repository = repository ?? new InvoicingWorkspaceRepository();
    }

    public InvoicingWorkspaceOverview? Overview { get; private set; }
    public bool IsLoading { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            Overview = await _repository.LoadOverviewAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            Overview = null;
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }
}
