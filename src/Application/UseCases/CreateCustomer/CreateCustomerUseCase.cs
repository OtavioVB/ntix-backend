using Application.UseCases.Base;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.CreateCustomer.Inputs;
using Domain.BoundedContexts.CustomerContext.Entities;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.SqlServer.Base.UnitOfWork.Interfaces;
using Infra.SqlServer.Repositories.Customers.Enums;
using Infra.SqlServer.Repositories.Customers.Interfaces;
using Infra.SqlServer.Repositories.Customers.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.UseCases.CreateCustomer;

public sealed class CreateCustomerUseCase : IUseCase<CreateCustomerUseCaseInput>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly CreateCustomerDiagnostics _diagnostics;
    private readonly ILogger<CreateCustomerUseCase> _logger;

    public CreateCustomerUseCase(
        ICustomerRepository customerRepository,
        IUnitOfWork unitOfWork,
        CreateCustomerDiagnostics diagnostics,
        ILogger<CreateCustomerUseCase> logger)
    {
        _customerRepository = customerRepository;
        _unitOfWork = unitOfWork;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    private const string CREATE_CUSTOMER_SUCCESS_NOTIFICATION_CODE = "CREATE_CUSTOMER_SUCCESS";
    private const string CREATE_CUSTOMER_SUCCESS_NOTIFICATION_MESSAGE = "O cliente foi cadastrado com sucesso.";

    private const string CREATE_CUSTOMER_ERROR_NOTIFICATION_CODE = "CREATE_CUSTOMER_ERROR";
    private const string CREATE_CUSTOMER_ERROR_NOTIFICATION_MESSAGE = "Não foi possível cadastrar o cliente.";

    private const string CUSTOMER_ALREADY_EXISTS_NOTIFICATION_CODE = "CUSTOMER_ALREADY_EXISTS";
    private const string CUSTOMER_ALREADY_EXISTS_NOTIFICATION_MESSAGE = "Já existe um cliente com o email ou o CPF informado.";

    public async Task<MethodResult> ExecuteAsync(CreateCustomerUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = StartActivity(auditableInfo);

        _diagnostics.RegistrationRequested();

        try
        {
            var validationResult = MethodResult.Factory(input.GetInputValidation(), auditableInfo.MethodResult);

            if (validationResult.IsError)
                return BuildInvalidDataResult(validationResult);

            var baseSlug = SlugValueObject.Build(input.FullName.Value);

            if (!baseSlug.IsValid)
                return BuildInvalidDataResult(baseSlug.MethodResult);

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var customerExists = await _customerRepository.ExistsByEmailOrDocumentAsync(
                email: input.Email.Value,
                document: input.Document.Value,
                cancellationToken: cancellationToken);

            if (customerExists)
                return await RejectCustomerAlreadyExistsAsync(input);

            var customer = BuildCustomer(input, auditableInfo, await GetAvailableSlugAsync(baseSlug, cancellationToken));
            var creationResult = await _customerRepository.CreateAsync(BuildCustomerModel(customer), cancellationToken);

            while (creationResult == CustomerCreationResult.SlugAlreadyExists)
            {
                CreateCustomerLog.SlugTaken(_logger, input.Email.GetObfuscatedValue());

                customer = BuildCustomer(input, auditableInfo, await GetAvailableSlugAsync(baseSlug.BuildRandomVersion(), cancellationToken));
                creationResult = await _customerRepository.CreateAsync(BuildCustomerModel(customer), cancellationToken);
            }

            if (creationResult == CustomerCreationResult.EmailOrDocumentAlreadyExists)
                return await RejectCustomerAlreadyExistsAsync(input);

            await _unitOfWork.CommitAsync(cancellationToken);

            return BuildSuccessResult(customer);
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                throw;

            CreateCustomerLog.Failed(_logger, ex);

            // Só o tipo: a mensagem de uma exceção de banco pode trazer dados pessoais.
            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            return BuildErrorResult();
        }
    }

    private static Activity? StartActivity(AuditableInfoValueObject auditableInfo)
    {
        var activity = CreateCustomerDiagnostics.ACTIVITY_SOURCE.StartActivity("CreateCustomer");

        if (activity is not null && auditableInfo.IsValid)
        {
            activity.SetTag("ntix.correlation_id", auditableInfo.CorrelationId);
            activity.SetTag("ntix.origin_service", auditableInfo.OriginService);
            activity.SetTag("ntix.execution_user", auditableInfo.ExecutionUser);
            activity.SetTag("ntix.requested_at", auditableInfo.RequestedAt);
        }

        return activity;
    }

    private void RecordOutcome(string outcome)
    {
        _diagnostics.RegistrationFinished(outcome);

        Activity.Current?.SetTag("ntix.outcome", outcome);
    }

    private async Task<SlugValueObject> GetAvailableSlugAsync(SlugValueObject slug, CancellationToken cancellationToken)
    {
        while (await _customerRepository.ExistsBySlugAsync(slug.Value, cancellationToken))
            slug = slug.BuildRandomVersion();

        return slug;
    }

    private async Task RollbackAsync()
    {
        try
        {
            await _unitOfWork.RollbackAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            CreateCustomerLog.RollbackFailed(_logger, ex);
        }
    }

    private static Customer BuildCustomer(CreateCustomerUseCaseInput input, AuditableInfoValueObject auditableInfo, SlugValueObject slug)
        => new(
            id: IdValueObject.Build(),
            fullName: input.FullName,
            email: input.Email,
            document: input.Document,
            slug: slug,
            createdAt: DateTimeValueObject.Build(DateTime.UtcNow),
            createdCorrelationId: IdValueObject.Build(auditableInfo.CorrelationId.ToString()));

    private static CustomerModel BuildCustomerModel(Customer customer)
        => new(
            Id: customer.Id.Value,
            FullName: customer.FullName.Value,
            Email: customer.Email.Value,
            Document: customer.Document.Value,
            Slug: customer.Slug.Value,
            CreatedAt: customer.CreatedAt.Value,
            CreatedCorrelationId: customer.CreatedCorrelationId.Value);

    private MethodResult BuildSuccessResult(Customer customer)
    {
        RecordOutcome(CreateCustomerDiagnostics.OUTCOME_CREATED);

        Activity.Current?.SetTag("ntix.customer_id", customer.Id.Value);

        CreateCustomerLog.Created(
            logger: _logger,
            customerId: customer.Id.Value,
            obfuscatedEmail: customer.Email.GetObfuscatedValue(),
            obfuscatedCpf: customer.Document.GetObfuscatedValue());

        var successNotification = Notification.Success(
            code: CREATE_CUSTOMER_SUCCESS_NOTIFICATION_CODE,
            message: CREATE_CUSTOMER_SUCCESS_NOTIFICATION_MESSAGE);

        return MethodResult.Success([successNotification]);
    }

    private MethodResult BuildInvalidDataResult(MethodResult validationResult)
    {
        RecordOutcome(CreateCustomerDiagnostics.OUTCOME_VALIDATION_FAILED);

        CreateCustomerLog.InvalidData(
            logger: _logger,
            notificationCodes: string.Join(", ", validationResult.Notifications.Select(notification => notification.Code)));

        return validationResult;
    }

    private async Task<MethodResult> RejectCustomerAlreadyExistsAsync(CreateCustomerUseCaseInput input)
    {
        await _unitOfWork.RollbackAsync(CancellationToken.None);

        RecordOutcome(CreateCustomerDiagnostics.OUTCOME_ALREADY_EXISTS);

        CreateCustomerLog.AlreadyExists(
            logger: _logger,
            obfuscatedEmail: input.Email.GetObfuscatedValue(),
            obfuscatedCpf: input.Document.GetObfuscatedValue());

        var alreadyExistsNotification = Notification.Error(
            code: CUSTOMER_ALREADY_EXISTS_NOTIFICATION_CODE,
            message: CUSTOMER_ALREADY_EXISTS_NOTIFICATION_MESSAGE);

        return MethodResult.Error([alreadyExistsNotification]);
    }

    private MethodResult BuildErrorResult()
    {
        RecordOutcome(CreateCustomerDiagnostics.OUTCOME_ERROR);

        var errorNotification = Notification.Error(
            code: CREATE_CUSTOMER_ERROR_NOTIFICATION_CODE,
            message: CREATE_CUSTOMER_ERROR_NOTIFICATION_MESSAGE);

        return MethodResult.Error([errorNotification]);
    }
}
