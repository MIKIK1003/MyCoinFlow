using MyCoinFlow.WinUI.Data;
using MyCoinFlow.WinUI.Models;

namespace MyCoinFlow.WinUI.ViewModels;

public sealed class InvoicingViewModel
{
    private readonly InvoicingWorkspaceRepository _repository;
    private readonly InvoicingMasterDataRepository _masterDataRepository;
    private readonly InvoicingDocumentRepository _documentRepository;

    public InvoicingViewModel(
        InvoicingWorkspaceRepository? repository = null,
        InvoicingMasterDataRepository? masterDataRepository = null,
        InvoicingDocumentRepository? documentRepository = null)
    {
        _repository = repository ?? new InvoicingWorkspaceRepository();
        _masterDataRepository = masterDataRepository ?? new InvoicingMasterDataRepository();
        _documentRepository = documentRepository ?? new InvoicingDocumentRepository(_masterDataRepository);
    }

    public InvoicingWorkspaceOverview? Overview { get; private set; }
    public BillableObjectsWorkspace? BillableObjects { get; private set; }
    public InvoicingDocumentWorkspace? Documents { get; private set; }
    public bool IsLoading { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task LoadAsync(
        DateOnly? effectiveDate = null,
        string? searchText = null,
        CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            Overview = await _repository.LoadOverviewAsync(cancellationToken);
            BillableObjects = await _masterDataRepository.LoadBillableObjectsAsync(
                effectiveDate ?? DateOnly.FromDateTime(DateTime.Today),
                searchText,
                cancellationToken);
            Documents = await _documentRepository.LoadDocumentsAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            Overview = null;
            BillableObjects = null;
            Documents = null;
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }
}
