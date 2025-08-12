namespace LimpiadorPlayas_Prueba1.Modelos;

public enum TipoResiduo { Plastico, Metal, Vidrio, Papel, EWaste, Red, Micro }
public enum AccionRequerida { Toque, Mantener, Doble }

public class Residuo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public TipoResiduo Tipo { get; set; }
    public AccionRequerida Accion { get; set; }

    // Movimiento y tamaño (unidades lógicas en un lienzo 1200x675)
    public double Velocidad { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Ancho { get; set; } = 96;
    public double Alto { get; set; } = 96;

    // Estado
    public bool Removido { get; set; }
    public bool LlegoOrilla { get; set; }
    public DateTime? UltimoToque { get; set; }

    // Sprite
    public string RutaSprite { get; set; } = "img/residuos/plastico.png";

    // Hitbox reducido al 80%
    public (double L, double T, double R, double B) Hitbox()
    {
        var rx = Ancho * 0.1; var ry = Alto * 0.1;
        return (X + rx, Y + ry, X + Ancho - rx, Y + Alto - ry);
    }
}
