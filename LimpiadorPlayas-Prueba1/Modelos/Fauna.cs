namespace LimpiadorPlayas_Prueba1.Modelos;

public enum TipoFauna { Pez, Gaviota, Tortuga, Medusa }

public class Fauna
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public TipoFauna Tipo { get; set; }

    public double X { get; set; }
    public double Y { get; set; }
    public double Ancho { get; set; } = 128;
    public double Alto { get; set; } = 128;

    public double VX { get; set; }  // velocidad en X
    public double VY { get; set; }  // velocidad en Y

    public string RutaSprite { get; set; } = "img/fauna/pez.png";

    public (double L, double T, double R, double B) Hitbox()
    {
        var rx = Ancho * 0.1; var ry = Alto * 0.1;
        return (X + rx, Y + ry, X + Ancho - rx, Y + Alto - ry);
    }
}
