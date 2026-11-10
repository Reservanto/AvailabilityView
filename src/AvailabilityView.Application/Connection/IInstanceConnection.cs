namespace AvailabilityView.Application.Connection;

/// <summary>Stav připojení instance k účtu merchanta (<c>docs/SPEC.md</c> sekce 6).</summary>
public interface IInstanceConnection
{
    /// <summary>Instance má přístupový údaj k účtu merchanta. Neříká, že je platný.</summary>
    bool IsConnected { get; }
}
