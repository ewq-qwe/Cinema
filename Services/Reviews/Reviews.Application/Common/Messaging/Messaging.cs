using MediatR;

namespace Reviews.Application.Common.Messaging;

/// <summary>Команда (запис/зміна/видалення) без результату.</summary>
public interface ICommand : IRequest;

/// <summary>Команда, що повертає результат (наприклад, Id створеного документа).</summary>
public interface ICommand<out TResponse> : IRequest<TResponse>;

/// <summary>Запит (лише читання, стан не змінює).</summary>
public interface IQuery<out TResponse> : IRequest<TResponse>;
