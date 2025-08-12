namespace LimpiadorPlayas_Prueba1.Modelos;

public class ConfigNivel
{
    public int Nivel { get; set; }

    // Duración del nivel en segundos (EstadoJuego usa DuracionSegundos)
    public int DuracionSegundos { get; set; }

    // Metas
    public double MetaLimpieza { get; set; }                 // 0..1
    public double? MetaClasificacion { get; set; }           // 0..1 (nullable)
    public int? MetaSaludArrecife { get; set; }              // 0..100 (nullable)

    // Spawner y velocidad
    public (double Min, double Max) AparicionesPorSegundo { get; set; }
    public double VelocidadBase { get; set; }

    // Puntuación y penalizaciones
    public int PenalizacionFaunaPuntos { get; set; } = 15;
    public int PenalizacionFaunaSalud { get; set; } = 5;
    public int PenalizacionClasificacion { get; set; } = 5;
    public int RecompensaAcierto { get; set; } = 10;

    // Conjuntos de objetos disponibles en el nivel
    public TipoResiduo[] ConjuntoResiduos { get; set; } = System.Array.Empty<TipoResiduo>();
    public TipoFauna[] ConjuntoFauna { get; set; } = System.Array.Empty<TipoFauna>();
}
