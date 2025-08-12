using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using LimpiadorPlayas_Prueba1.Modelos;

namespace LimpiadorPlayas_Prueba1.Servicios
{
    public class EstadoJuego : IDisposable
    {
        // Notificación para refrescar UI
        public event Action? CambioEstado;

        private readonly IJSRuntime _js;
        private NavigationManager _nav;

        public EstadoJuego(IJSRuntime js, NavigationManager nav)
        {
            _js = js;
            _nav = nav;
        }

        // Área lógica del juego (16:9)
        public const double VW = 1200;
        public const double VH = 675;

        // Estado principal
        public int Nivel { get; private set; } = 1;
        public List<Residuo> Residuos { get; } = new();
        public List<Fauna> Faunas { get; } = new();
        public string? BandaEvento { get; private set; }
        public bool Pausado { get; private set; }

        // Bucle / tiempo
        private PeriodicTimer? _bucle;
        private CancellationTokenSource? _cts;
        public int SegundosRestantes { get; private set; }
        public double PorcentajeTiempo => 100.0 * Math.Max(0, SegundosRestantes) / ConfigActual.DuracionSegundos;

        // Puntuaciones
        public int Puntos { get; private set; }
        public int SaludArrecife { get; private set; } = 100;
        private int _clasifOk, _clasifTotal, _removidos, _aparecidos;
        public double PrecisionClasificacion => _clasifTotal == 0 ? 1.0 : (double)_clasifOk / _clasifTotal;

        // Combo
        private readonly Stopwatch _comboCrono = new();
        private int _nivelCombo = 0; // 0=sin combo, 1=+10%, 2=+20%, 3=+40%
        public string EtiquetaCombo => _nivelCombo switch { 1 => "+10%", 2 => "+20%", 3 => "+40%", _ => "x1" };

        // Clasificación
        public readonly string[] OpcionesClasificar = new[] { "Plástico", "Metal", "Vidrio", "Papel", "Orgánico", "E‑waste" };
        public bool ModoPinza { get; set; }
        public (bool Visible, string Estilo, string[] Opciones, Guid IdResiduo) SuperposicionClasificar { get; private set; }

        // Control de hold 1s
        private readonly ConcurrentDictionary<Guid, DateTime> _tiemposPresion = new();

        // Eventos temporales (corrientes, marea, etc.)
        private readonly List<Evento> _eventosActivos = new();

        // Configuración de niveles
        public ConfigNivel ConfigActual { get; private set; } = Configs[1];

        public static readonly Dictionary<int, ConfigNivel> Configs = new()
        {
            { 1, new ConfigNivel {
                Nivel=1, DuracionSegundos=60, MetaLimpieza=0.80,
                AparicionesPorSegundo=(1,2), VelocidadBase=40,
                ConjuntoResiduos = new[]{ TipoResiduo.Plastico, TipoResiduo.Papel, TipoResiduo.Metal },
                ConjuntoFauna = new[]{ TipoFauna.Pez, TipoFauna.Gaviota }
            }},
            { 2, new ConfigNivel {
                Nivel=2, DuracionSegundos=75, MetaLimpieza=0.85, MetaClasificacion=0.60,
                AparicionesPorSegundo=(2,3), VelocidadBase=60,
                ConjuntoResiduos = new[]{ TipoResiduo.Plastico, TipoResiduo.Papel, TipoResiduo.Metal, TipoResiduo.Red, TipoResiduo.Micro },
                ConjuntoFauna = new[]{ TipoFauna.Pez, TipoFauna.Gaviota, TipoFauna.Tortuga }
            }},
            { 3, new ConfigNivel {
                Nivel=3, DuracionSegundos=90, MetaLimpieza=0.90, MetaClasificacion=0.70, MetaSaludArrecife=70,
                AparicionesPorSegundo=(3,4), VelocidadBase=80,
                ConjuntoResiduos = new[]{ TipoResiduo.Plastico, TipoResiduo.Papel, TipoResiduo.Metal, TipoResiduo.Vidrio, TipoResiduo.EWaste, TipoResiduo.Red, TipoResiduo.Micro },
                ConjuntoFauna = new[]{ TipoFauna.Pez, TipoFauna.Tortuga, TipoFauna.Medusa }
            }},
        };

        // Inicialización (el nivel lo recibimos desde el componente)
        public Task Inicializar(int nivel, NavigationManager nav)
        {
            _nav = nav;
            Nivel = Math.Clamp(nivel, 1, 3);
            ConfigActual = Configs[Nivel];
            SegundosRestantes = ConfigActual.DuracionSegundos;
            return Task.CompletedTask;
        }

        public Task IniciarNivelAsync()
        {
            ReiniciarEstado();
            _cts = new CancellationTokenSource();
            _bucle = new PeriodicTimer(TimeSpan.FromMilliseconds(33)); // ~30 FPS
            _ = EjecutarBucleAsync(_cts.Token);
            return Task.CompletedTask;
        }

        private void ReiniciarEstado()
        {
            Residuos.Clear(); Faunas.Clear();
            _aparecidos = 0; _removidos = 0; Puntos = 0;
            SaludArrecife = 100; _clasifOk = 0; _clasifTotal = 0;
            _nivelCombo = 0; _comboCrono.Reset();
            SuperposicionClasificar = default;
            BandaEvento = null; _eventosActivos.Clear();
            Pausado = false;
        }

        private async Task EjecutarBucleAsync(CancellationToken token)
        {
            var rng = new Random();
            var ultimoSpawn = DateTime.UtcNow;
            var ultimoSegundo = DateTime.UtcNow;
            var evento1 = false; var evento2 = false;

            while (await _bucle!.WaitForNextTickAsync(token))
            {
                if (Pausado) { CambioEstado?.Invoke(); continue; }
                var ahora = DateTime.UtcNow;

                // Spawner
                var apariciones = rng.NextDouble().Map(ConfigActual.AparicionesPorSegundo.Min, ConfigActual.AparicionesPorSegundo.Max);
                var intervalo = 1.0 / apariciones;
                if ((ahora - ultimoSpawn).TotalSeconds >= intervalo)
                {
                    CrearResiduo(rng);
                    if (rng.NextDouble() < 0.15) CrearFauna(rng);
                    ultimoSpawn = ahora;
                }

                // Movimiento residuos
                var modVel = ModificadorVelocidadActivo();
                foreach (var r in Residuos.ToList())
                {
                    r.Y += (ConfigActual.VelocidadBase * modVel + r.Velocidad) * 0.033; // 33ms
                    if (r.Y + r.Alto >= VH) r.LlegoOrilla = true;
                }

                // Movimiento fauna
                foreach (var f in Faunas.ToList())
                {
                    f.X += f.VX * 0.033; f.Y += f.VY * 0.033;
                    if (f.X < 0 || f.X + f.Ancho > VW) f.VX = -f.VX;
                    if (f.Y < 0 || f.Y + f.Alto > VH * 0.8) f.VY = -f.VY;
                }

                // Cuenta atrás + eventos
                if ((ahora - ultimoSegundo).TotalSeconds >= 1)
                {
                    SegundosRestantes--;
                    ultimoSegundo = ahora;

                    if (Nivel >= 2 && !evento1 && SegundosRestantes <= (int)(ConfigActual.DuracionSegundos * 0.66))
                    {
                        LanzarEvento(new Evento
                        {
                            Tipo = TipoEvento.Corriente,
                            Duracion = TimeSpan.FromSeconds(12),
                            ModificadorVelocidad = 1.35,
                            Banner = "Corrientes fuertes"
                        });
                        evento1 = true;
                    }

                    if (Nivel == 3 && !evento2 && SegundosRestantes <= 10)
                    {
                        LanzarEvento(new Evento
                        {
                            Tipo = TipoEvento.MareaAlta,
                            Duracion = TimeSpan.FromSeconds(10),
                            ModificadorVelocidad = 1.6,
                            Banner = "Marea alta"
                        });
                        evento2 = true;
                    }

                    if (SegundosRestantes <= 0) { FinalizarNivel(); return; }
                }

                // Limpieza de eventos y banner
                _eventosActivos.RemoveAll(e => !e.Activo);
                BandaEvento = _eventosActivos.Count > 0 ? _eventosActivos.Last().Banner : null;

                CambioEstado?.Invoke();
            }
        }

        // Código para la creación de residuos, fauna, eventos, y más...
        private void CrearResiduo(Random rng)
        {
            var t = ConfigActual.ConjuntoResiduos[rng.Next(ConfigActual.ConjuntoResiduos.Length)];
            var accion = t switch
            {
                TipoResiduo.Red => AccionRequerida.Mantener,
                TipoResiduo.Vidrio => AccionRequerida.Doble,
                TipoResiduo.Micro => AccionRequerida.Toque,
                _ => AccionRequerida.Toque
            };

            var r = new Residuo
            {
                Tipo = t,
                Accion = accion,
                Velocidad = rng.NextDouble() * 10,
                X = rng.NextDouble() * (VW - 100),
                Y = -100,
                Ancho = t switch { TipoResiduo.Red => 128, TipoResiduo.Micro => 48, _ => 96 },
                Alto = t switch { TipoResiduo.Red => 128, TipoResiduo.Micro => 48, _ => 96 },
                RutaSprite = RutaSpriteResiduo(t)
            };

            Residuos.Add(r);
            _aparecidos++;
        }

        private void CrearFauna(Random rng)
        {
            var t = ConfigActual.ConjuntoFauna[rng.Next(ConfigActual.ConjuntoFauna.Length)];
            var f = new Fauna
            {
                Tipo = t,
                X = rng.NextDouble() * (VW - 160),
                Y = rng.NextDouble() * (VH * 0.6),
                Ancho = t == TipoFauna.Tortuga ? 160 : (t == TipoFauna.Medusa ? 128 : 128),
                Alto = t == TipoFauna.Tortuga ? 128 : (t == TipoFauna.Medusa ? 160 : 128),
                VX = rng.Next(-20, 20),
                VY = rng.Next(-10, 10),
                RutaSprite = RutaSpriteFauna(t)
            };
            Faunas.Add(f);
        }

        private double ModificadorVelocidadActivo()
        {
            double mod = 1.0;
            foreach (var e in _eventosActivos) mod *= e.ModificadorVelocidad;
            return mod;
        }

        private void LanzarEvento(Evento ev)
        {
            ev.InicioUtc = DateTime.UtcNow;
            _eventosActivos.Add(ev);
            _ = _js.InvokeVoidAsync("interop.play", "sonido-evento");
        }

        private string RutaSpriteResiduo(TipoResiduo t) => t switch
        {
            TipoResiduo.Plastico => "img/residuos/plastico.png",
            TipoResiduo.Metal => "img/residuos/metal.png",
            TipoResiduo.Vidrio => "img/residuos/vidrio.png",
            TipoResiduo.Papel => "img/residuos/papel.png",
            TipoResiduo.EWaste => "img/residuos/residuo-electronico.png",
            TipoResiduo.Red => "img/residuos/red-fantasma.png",
            TipoResiduo.Micro => "img/residuos/microplasticos.png",
            _ => "img/residuos/plastico.png"
        };

        private string RutaSpriteFauna(TipoFauna t) => t switch
        {
            TipoFauna.Pez => "img/fauna/pez.png",
            TipoFauna.Gaviota => "img/fauna/gaviota.png",
            TipoFauna.Tortuga => "img/fauna/tortuga.png",
            TipoFauna.Medusa => "img/fauna/medusa.png",
            _ => "img/fauna/pez.png"
        };

        // Interacciones (toque, doble toque, mantener 1s)
        public void Presionar(Guid id) => _tiemposPresion[id] = DateTime.UtcNow;

        public void Soltar(Guid id)
        {
            if (_tiemposPresion.TryRemove(id, out var inicio))
            {
                var sostenido = DateTime.UtcNow - inicio;
                var r = Residuos.FirstOrDefault(x => x.Id == id);
                if (r is null || r.Removido) return;
                if (ModoPinza || r.Accion != AccionRequerida.Mantener) return;
                if (sostenido.TotalMilliseconds >= 1000) QuitarYMostrarClasificar(r);
            }
        }

        public void Toque(Guid id)
        {
            var r = Residuos.FirstOrDefault(x => x.Id == id);
            if (r is null || r.Removido) return;

            if (ModoPinza)
            {
                QuitarYMostrarClasificar(r);
                return;
            }

            if (r.Accion == AccionRequerida.Doble)
            {
                var ahora = DateTime.UtcNow;
                if (r.UltimoToque is not null && (ahora - r.UltimoToque.Value).TotalMilliseconds <= 300)
                {
                    QuitarYMostrarClasificar(r);
                    r.UltimoToque = null;
                }
                else
                {
                    r.UltimoToque ??= ahora;
                }
            }
            else if (r.Accion == AccionRequerida.Toque)
            {
                QuitarYMostrarClasificar(r);
            }
        }

        private void QuitarYMostrarClasificar(Residuo r)
        {
            r.Removido = true;
            _removidos++;
            _ = _js.InvokeVoidAsync("interop.play", "sonido-recoger");

            var leftPct = r.X / VW * 100.0;
            var topPct = Math.Max(5, (r.Y / VH * 100.0) - 8);
            var estilo = $"left:{leftPct}%; top:{topPct}%; z-index:3; transform: translate(-50%,-110%);";
            SuperposicionClasificar = (true, estilo, OpcionesClasificar, r.Id);

            CambioEstado?.Invoke();
            VerificarMetaLimpiezaParcial();
        }

        public void Clasificar(string opcion)
        {
            if (!SuperposicionClasificar.Visible) return;
            var r = Residuos.FirstOrDefault(x => x.Id == SuperposicionClasificar.IdResiduo);
            SuperposicionClasificar = default;
            if (r is null) return;

            _clasifTotal++;
            var correcto = OpcionParaResiduo(r.Tipo) == opcion;

            if (correcto)
            {
                _clasifOk++;
                var baseReward = ConfigActual.RecompensaAcierto;
                var mult = _nivelCombo switch { 1 => 1.1, 2 => 1.2, 3 => 1.4, _ => 1.0 };
                Puntos += (int)Math.Round(baseReward * mult);
                AvanzarCombo();
            }
            else
            {
                Puntos -= ConfigActual.PenalizacionClasificacion;
                RomperCombo();
                _ = _js.InvokeVoidAsync("interop.play", "sonido-error");
            }

            CambioEstado?.Invoke();
        }

        private string OpcionParaResiduo(TipoResiduo t) => t switch
        {
            TipoResiduo.Plastico => "Plástico",
            TipoResiduo.Metal => "Metal",
            TipoResiduo.Vidrio => "Vidrio",
            TipoResiduo.Papel => "Papel",
            TipoResiduo.EWaste => "E‑waste",
            TipoResiduo.Red => "Plástico",
            TipoResiduo.Micro => "Plástico",
            _ => "Plástico"
        };

        private void AvanzarCombo()
        {
            if (!_comboCrono.IsRunning) _comboCrono.Start();
            if (_comboCrono.ElapsedMilliseconds <= 4000)
                _nivelCombo = Math.Min(3, _nivelCombo + 1);
            else
                _nivelCombo = 1;

            _comboCrono.Restart();
        }

        private void RomperCombo()
        {
            _nivelCombo = 0;
            _comboCrono.Reset();
        }

        private void VerificarMetaLimpiezaParcial()
        {
            var total = Math.Max(_aparecidos, 1);
            var pct = (double)_removidos / total;
            if (pct >= ConfigActual.MetaLimpieza && SegundosRestantes <= 5)
                FinalizarNivel();
        }

        private void FinalizarNivel()
        {
            _cts?.Cancel();

            var total = Math.Max(_aparecidos, 1);
            var pct = (double)_removidos / total;

            var okLimpieza = pct >= ConfigActual.MetaLimpieza;
            var okClasif = ConfigActual.MetaClasificacion is null || PrecisionClasificacion >= ConfigActual.MetaClasificacion.Value;
            var okArrecife = ConfigActual.MetaSaludArrecife is null || SaludArrecife >= ConfigActual.MetaSaludArrecife.Value;

            var victoria = okLimpieza && okClasif && okArrecife;

            // Generar la URL para la página de resumen
            var uri = $"/resumen?nivel={Nivel}&puntos={Puntos}&limpio={(int)(pct * 100)}&precision={(int)(PrecisionClasificacion * 100)}&arrecife={SaludArrecife}&victoria={(victoria ? 1 : 0)}";
            Console.WriteLine($"Navegando a: {uri}"); // Debugging para verificar la URL
            _nav.NavigateTo(uri, forceLoad: true);
        }

        public void PausarReanudar() => Pausado = !Pausado;

        // Estilos de posicionamiento relativos al contenedor 16:9
        public string Estilo(Residuo r)
        {
            var left = r.X / VW * 100.0;
            var top = r.Y / VH * 100.0;
            var w = r.Ancho / VW * 100.0;
            return $"left:{left}%; top:{top}%; width:{w}%;";
        }

        public string Estilo(Fauna f)
        {
            var left = f.X / VW * 100.0;
            var top = f.Y / VH * 100.0;
            var w = f.Ancho / VW * 100.0;
            return $"left:{left}%; top:{top}%; width:{w}%;";
        }

        public void Dispose() => _cts?.Cancel();

    }
    // Clase de extensión para Map
    public static class Extensiones
    {
        // Método de extensión para mapear un valor entre un rango dado
        public static double Map(this double r, double min, double max)
        {
            r = Math.Clamp(r, 0, 1); // Limita el valor entre 0 y 1
            return min + r * (max - min); // Mapea el valor de r al rango entre min y max
        }
    }

}
