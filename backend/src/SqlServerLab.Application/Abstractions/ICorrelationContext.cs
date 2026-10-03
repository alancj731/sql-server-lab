namespace SqlServerLab.Application.Abstractions;

public interface ICorrelationContext
{
    string CorrelationId { get; }
}
