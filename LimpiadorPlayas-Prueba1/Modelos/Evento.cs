namespace LimpiadorPlayas_Prueba1.Modelos;

public enum TipoEvento { Corriente, MareaAlta, Medusas }

public class Evento
{
    public TipoEvento Tipo { get; set; }
    public TimeSpan Duracion { get; set; }
    public double ModificadorVelocidad { get; set; } = 1.0;
    public string Banner { get; set; } = string.Empty;

    public DateTime InicioUtc { get; set; } = DateTime.UtcNow;
    public bool Activo => DateTime.UtcNow - InicioUtc < Duracion;
}
