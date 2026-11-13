using System.Numerics;
using ElectricSim.Core.Components;

namespace ElectricSim.Core;

/// <summary>
/// Lo que el solver de fasores le dice a un componente de una red de alterna:
/// valores RMS (eficaces), potencia ACTIVA y la fase entre su tensión y su
/// corriente. Es lo que el inspector muestra como "en alterna".
/// </summary>
public sealed class ResultadoAlterna
{
    /// <summary>Tensión eficaz entre sus bornes (V).</summary>
    public double VoltajeRms { get; }

    /// <summary>Corriente eficaz que lo atraviesa (A).</summary>
    public double CorrienteRms { get; }

    /// <summary>Potencia ACTIVA P = Re(V·I*): la que se vuelve calor o trabajo (W).</summary>
    public double PotenciaActiva { get; }

    /// <summary>Potencia APARENTE S = V·I (VA). Es mayor que P cuando hay reactancia.</summary>
    public double PotenciaAparente { get; }

    /// <summary>Fase de la tensión respecto de la corriente (°). Positiva = inductivo (la corriente se atrasa), negativa = capacitivo.</summary>
    public double FaseGrados { get; }

    /// <summary>cos φ = P/S. 1 en una resistencia pura, ~0 en un capacitor o inductor ideal.</summary>
    public double FactorPotencia =>
        PotenciaAparente <= 1e-12 ? 0 : Math.Min(1.0, PotenciaActiva / PotenciaAparente);

    /// <summary>Potencia REACTIVA Q (VAr): la que va y vuelve al campo sin hacer trabajo.</summary>
    public double PotenciaReactiva =>
        Math.Sqrt(Math.Max(0, PotenciaAparente * PotenciaAparente - PotenciaActiva * PotenciaActiva));

    public ResultadoAlterna(double voltajeRms, double corrienteRms, double potenciaActiva,
                            double potenciaAparente, double faseGrados)
    {
        VoltajeRms = voltajeRms;
        CorrienteRms = corrienteRms;
        PotenciaActiva = potenciaActiva;
        PotenciaAparente = potenciaAparente;
        FaseGrados = faseGrados;
    }
}

/// <summary>
/// SOLVER DE FASORES para redes de alterna que tienen un capacitor o un
/// inductor. Resuelve el régimen sinusoidal permanente con la matriz nodal
/// compleja (análisis nodal modificado, sólo sobre la sub-red de alterna):
///
///     Capacitor:  Z = ESR + 1/(jωC)         → Xc = 1/(2π·f·C)
///     Inductor:   Z = R_bobinado + jωL      → XL = 2π·f·L
///     Resistivos: Z = R
///     Fuente:     E = V_rms, con su resistencia interna en serie
///
/// POR QUÉ EXISTE. El solver principal trabaja en RMS y trata la alterna como
/// si fuera continua. Para cargas resistivas eso es EXACTO (el valor eficaz se
/// define justo así), pero un capacitor o un inductor dependen de la
/// frecuencia, y sin ella: el inductor se comportaba como un corto de 0.8Ω
/// (151A donde debían circular 0.70A) y el capacitor no dejaba pasar nada
/// (0A donde debían ser 0.69A).
///
/// QUÉ NO TOCA. Las redes de continua y las de alterna puramente resistiva
/// NO pasan por acá: el solver RMS ya da el resultado exacto y no hay motivo
/// para arriesgarlo. Sólo se activa cuando una red de alterna tiene un
/// capacitor o inductor Y todos sus componentes son lineales pasivos. Si hay
/// algo que no se sabe modelar en fasores (un convertidor, un semiconductor
/// compatible con alterna), la red se deja como estaba.
///
/// LÍMITES CONSCIENTES: una sola frecuencia por red (la de la primera fuente),
/// y los cables fusionados de una rama reactiva siguen mostrando la corriente
/// del solver RMS (no afecta el cálculo: sólo es lo que se ve en el cable).
/// </summary>
internal static class SolverAlterna
{
    private sealed class Rama
    {
        public Component Componente = null!;
        public Pin A = null!;
        public Pin B = null!;
        public Complex Admitancia;
    }

    /// <summary>Resuelve cada red de alterna con elementos reactivos y deja el resultado en `ResultadoAC`.</summary>
    internal static void Resolver(IReadOnlyList<Component> componentes, UnionFind uf)
    {
        foreach (var c in componentes) c.ResultadoAC = null;

        // 1) los componentes por los que circula alterna y que conducen
        var activos = new List<Component>();
        bool hayReactivo = false;
        foreach (var c in componentes)
        {
            if (c.IsBurnt || c.EsIncompatible || c.TipoCorrienteRecibida != TipoCorriente.AC) continue;
            activos.Add(c);
            if (c is Capacitor || c is Inductor) hayReactivo = true;
        }
        if (!hayReactivo) return; // lo más común: no se hace NADA

        // las aristas internas de un potenciómetro o convertidor también son pasivas lineales
        var internosPasivos = new HashSet<Component>();
        foreach (var c in componentes)
        {
            if (c is Potenciometro pote)
            {
                foreach (var interna in pote.AristasInternas) internosPasivos.Add(interna);
            }
            else if (c is ConvertidorAislado conv)
            {
                foreach (var interna in conv.AristasInternas) internosPasivos.Add(interna);
            }
        }

        bool EsFuente(Component c) =>
            c is FuenteBase f && f.Tipo == TipoCorriente.AC && f.EstaEntregandoTipo;

        bool Soportado(Component c) =>
            EsFuente(c)
            || c is Resistor || c is Lampara || c is Switch || c is Pulsador
            || c is Fusible || c is Wire
            || c is Potenciometro || internosPasivos.Contains(c)
            || c is Capacitor || c is Inductor || c is CajaConPantalla;

        // 2) los que son aristas reales (los que quedaron en un solo nodo no cuentan)
        var elementos = new List<Component>();
        foreach (var c in activos)
        {
            if (!ReferenceEquals(uf.Encontrar(c.PinA), uf.Encontrar(c.PinB))) elementos.Add(c);
        }

        // 3) agrupar en redes conexas (union-find local: no toca el del solver)
        var padre = new Dictionary<Pin, Pin>();
        Pin Raiz(Pin x)
        {
            if (!padre.ContainsKey(x)) padre[x] = x;
            while (!ReferenceEquals(padre[x], x))
            {
                padre[x] = padre[padre[x]];
                x = padre[x];
            }
            return x;
        }
        foreach (var c in elementos)
        {
            var ra = Raiz(uf.Encontrar(c.PinA));
            var rb = Raiz(uf.Encontrar(c.PinB));
            if (!ReferenceEquals(ra, rb)) padre[ra] = rb;
        }
        var grupos = new Dictionary<Pin, List<Component>>();
        foreach (var c in elementos)
        {
            var clave = Raiz(uf.Encontrar(c.PinA));
            if (!grupos.TryGetValue(clave, out var lista)) grupos[clave] = lista = new List<Component>();
            lista.Add(c);
        }

        // 4) resolver cada red
        foreach (var comps in grupos.Values)
        {
            bool conReactivo = false, todoSoportado = true;
            foreach (var c in comps)
            {
                if (c is Capacitor || c is Inductor) conReactivo = true;
                if (!Soportado(c)) todoSoportado = false;
            }
            if (!conReactivo || !todoSoportado) continue;

            var fuentes = new List<FuenteBase>();
            foreach (var c in comps)
            {
                if (EsFuente(c)) fuentes.Add((FuenteBase)c);
            }
            if (fuentes.Count == 0) continue;

            double frecuencia = fuentes[0] is FuenteAC fuenteAc ? fuenteAc.Frecuencia
                              : fuentes[0] is Inversor inv ? inv.FrecuenciaSalida
                              : 50.0;
            double omega = 2.0 * Math.PI * frecuencia;

            // admitancia de cada rama
            var ramas = new List<Rama>();
            foreach (var c in comps)
            {
                if (EsFuente(c) || c is CajaConPantalla) continue;

                Complex z;
                if (c is Capacitor cap)
                {
                    if (omega <= 0) continue;
                    z = new Complex(cap.ResistenciaSerie, -1.0 / (omega * Math.Max(cap.Capacitance, 1e-12)));
                }
                else if (c is Inductor ind)
                {
                    z = new Complex(ind.ResistenciaBobinado, omega * ind.Inductancia);
                }
                else
                {
                    double r = c.ResistenciaParaSolver();
                    if (r >= CircuitConstants.OpenResistance * 0.5) continue;
                    z = new Complex(Math.Max(r, CircuitConstants.MinResistance), 0);
                }

                ramas.Add(new Rama
                {
                    Componente = c,
                    A = uf.Encontrar(c.PinA),
                    B = uf.Encontrar(c.PinB),
                    Admitancia = Complex.Reciprocal(z),
                });
            }

            // nodos de la red; el de referencia es el borne B de la primera fuente
            var nodos = new List<Pin>();
            void AgregarNodo(Pin n)
            {
                foreach (var existente in nodos)
                {
                    if (ReferenceEquals(existente, n)) return;
                }
                nodos.Add(n);
            }
            foreach (var r in ramas) { AgregarNodo(r.A); AgregarNodo(r.B); }
            foreach (var f in fuentes) { AgregarNodo(uf.Encontrar(f.PinA)); AgregarNodo(uf.Encontrar(f.PinB)); }

            var referencia = uf.Encontrar(fuentes[0].PinB);
            var indice = new Dictionary<Pin, int>();
            foreach (var n in nodos)
            {
                if (!ReferenceEquals(n, referencia)) indice[n] = indice.Count;
            }
            int cantNodos = indice.Count;
            int cantFuentes = fuentes.Count;
            int tam = cantNodos + cantFuentes;

            int Indice(Pin n) => indice.TryGetValue(n, out int i) ? i : -1;

            var a = new Complex[tam, tam];
            var b = new Complex[tam];

            foreach (var r in ramas)
            {
                int ia = Indice(r.A), ib = Indice(r.B);
                if (ia >= 0) a[ia, ia] += r.Admitancia;
                if (ib >= 0) a[ib, ib] += r.Admitancia;
                if (ia >= 0 && ib >= 0)
                {
                    a[ia, ib] -= r.Admitancia;
                    a[ib, ia] -= r.Admitancia;
                }
            }

            // cada fuente: V(+) − V(−) − R_int·I = E, e inyecta I en su nodo +
            for (int k = 0; k < cantFuentes; k++)
            {
                var f = fuentes[k];
                int m = cantNodos + k;
                int ip = Indice(uf.Encontrar(f.PinA));
                int iN = Indice(uf.Encontrar(f.PinB));
                if (ip >= 0) { a[ip, m] -= Complex.One; a[m, ip] += Complex.One; }
                if (iN >= 0) { a[iN, m] += Complex.One; a[m, iN] -= Complex.One; }
                a[m, m] = new Complex(-f.ResistenciaInternaNominal, 0);
                b[m] = new Complex(f.Voltage, 0);
            }

            Complex[] x;
            if (!ResolverSistema(a, b, out x)) continue; // red flotante o degenerada: se deja como estaba

            Complex Tension(Pin n) => ReferenceEquals(n, referencia) ? Complex.Zero : x[indice[n]];

            foreach (var r in ramas)
            {
                Complex dv = Tension(r.A) - Tension(r.B);
                Complex corriente = r.Admitancia * dv;
                double potenciaActiva = (dv * Complex.Conjugate(corriente)).Real;
                double potenciaAparente = Complex.Abs(dv) * Complex.Abs(corriente);
                double fase = (dv.Phase - corriente.Phase) * 180.0 / Math.PI;

                r.Componente.ResultadoAC = new ResultadoAlterna(
                    Complex.Abs(dv), Complex.Abs(corriente),
                    Math.Max(potenciaActiva, 0), potenciaAparente, fase);
            }

            for (int k = 0; k < cantFuentes; k++)
            {
                fuentes[k].CorrienteEntregada = Complex.Abs(x[cantNodos + k]);
            }

            // Propagar corrientes fasoriales a las conexiones fusionadas de
            // esta red (cables, interruptores cerrados y CajaConPantalla).
            var conjuntoNodos = new HashSet<Pin>(nodos);
            var inyeccionAc = new Dictionary<Pin, Complex>();
            void InyectarAc(Pin pin, Complex corr)
            {
                inyeccionAc[pin] = inyeccionAc.GetValueOrDefault(pin) + corr;
            }

            for (int k = 0; k < cantFuentes; k++)
            {
                var f = fuentes[k];
                Complex iFuente = x[cantNodos + k];
                InyectarAc(f.PinA, iFuente);
                InyectarAc(f.PinB, -iFuente);
            }
            foreach (var r in ramas)
            {
                Complex dv = Tension(r.A) - Tension(r.B);
                Complex corr = r.Admitancia * dv;
                InyectarAc(r.Componente.PinA, -corr);
                InyectarAc(r.Componente.PinB, corr);
            }

            var aristasPorPin = new Dictionary<Pin, List<(Pin otro, Component porComponente)>>();
            var pinesPorRep = new Dictionary<Pin, List<Pin>>();
            void RegistrarPin(Pin p)
            {
                var rep = uf.Encontrar(p);
                if (!conjuntoNodos.Contains(rep)) return;
                if (!pinesPorRep.TryGetValue(rep, out var lp)) pinesPorRep[rep] = lp = new();
                if (!lp.Contains(p)) lp.Add(p);
            }
            foreach (var f in fuentes) { RegistrarPin(f.PinA); RegistrarPin(f.PinB); }
            foreach (var r in ramas) { RegistrarPin(r.Componente.PinA); RegistrarPin(r.Componente.PinB); }
            foreach (var c in componentes)
            {
                if (!c.FusionadoEsteTick) continue;
                var rep = uf.Encontrar(c.PinA);
                if (!conjuntoNodos.Contains(rep)) continue;
                RegistrarPin(c.PinA);
                RegistrarPin(c.PinB);
                if (ReferenceEquals(c.PinA, c.PinB)) continue;
                if (!aristasPorPin.TryGetValue(c.PinA, out var la)) aristasPorPin[c.PinA] = la = new();
                la.Add((c.PinB, c));
                if (!aristasPorPin.TryGetValue(c.PinB, out var lb)) aristasPorPin[c.PinB] = lb = new();
                lb.Add((c.PinA, c));
            }

            Complex vFuentePrincipal = Tension(uf.Encontrar(fuentes[0].PinA)) - Tension(uf.Encontrar(fuentes[0].PinB));
            foreach (var (rep, grupo) in pinesPorRep)
            {
                if (grupo.Count < 2) continue;
                var padrePin = new Dictionary<Pin, (Pin pin, Component rama)?> { [grupo[0]] = null };
                var orden = new List<Pin> { grupo[0] };
                for (int i = 0; i < orden.Count; i++)
                {
                    if (!aristasPorPin.TryGetValue(orden[i], out var vecinos)) continue;
                    foreach (var (otro, porComp) in vecinos)
                    {
                        if (padrePin.ContainsKey(otro)) continue;
                        padrePin[otro] = (orden[i], porComp);
                        orden.Add(otro);
                    }
                }

                var acum = new Dictionary<Pin, Complex>();
                foreach (var p in orden) acum[p] = inyeccionAc.GetValueOrDefault(p);

                for (int i = orden.Count - 1; i > 0; i--)
                {
                    var pin = orden[i];
                    var (pinPadre, rama) = padrePin[pin]!.Value;
                    Complex flujoHaciaPadre = acum[pin];
                    acum[pinPadre] += flujoHaciaPadre;

                    Complex iRama = ReferenceEquals(rama.PinA, pin) ? flujoHaciaPadre : -flujoHaciaPadre;
                    rama.CorrienteFusionada = Complex.Abs(iRama);

                    if (rama is CajaConPantalla caja)
                    {
                        Complex vRef = Tension(rep);
                        if (Complex.Abs(vRef) <= 1e-4 && Complex.Abs(iRama) > 1e-6)
                        {
                            vRef = vFuentePrincipal;
                        }
                        Complex sCompleja = vRef * Complex.Conjugate(iRama);
                        if (sCompleja.Real < 0)
                        {
                            sCompleja = -sCompleja;
                        }
                        double vRms = Complex.Abs(vRef);
                        double iRms = Complex.Abs(iRama);
                        double pActiva = Math.Max(sCompleja.Real, 0);
                        double pAparente = vRms * iRms;
                        double fase = pAparente > 1e-12
                            ? Math.Atan2(sCompleja.Imaginary, sCompleja.Real) * 180.0 / Math.PI
                            : 0.0;
                        caja.FrecuenciaHz = frecuencia;
                        caja.ResultadoAC = new ResultadoAlterna(vRms, iRms, pActiva, pAparente, fase);
                    }
                }
            }
        }
    }

    /// <summary>Eliminación gaussiana con pivoteo parcial sobre complejos. Devuelve false si la matriz es singular.</summary>
    private static bool ResolverSistema(Complex[,] a, Complex[] b, out Complex[] x)
    {
        int n = b.Length;
        x = new Complex[n];

        for (int k = 0; k < n; k++)
        {
            int pivote = k;
            double mejor = Complex.Abs(a[k, k]);
            for (int i = k + 1; i < n; i++)
            {
                double m = Complex.Abs(a[i, k]);
                if (m > mejor) { mejor = m; pivote = i; }
            }
            if (mejor < 1e-15) return false;

            if (pivote != k)
            {
                for (int j = 0; j < n; j++)
                {
                    Complex tmp = a[k, j];
                    a[k, j] = a[pivote, j];
                    a[pivote, j] = tmp;
                }
                Complex tb = b[k];
                b[k] = b[pivote];
                b[pivote] = tb;
            }

            for (int i = k + 1; i < n; i++)
            {
                Complex factor = a[i, k] / a[k, k];
                if (factor == Complex.Zero) continue;
                for (int j = k; j < n; j++) a[i, j] -= factor * a[k, j];
                b[i] -= factor * b[k];
            }
        }

        for (int i = n - 1; i >= 0; i--)
        {
            Complex acumulado = b[i];
            for (int j = i + 1; j < n; j++) acumulado -= a[i, j] * x[j];
            x[i] = acumulado / a[i, i];
        }
        return true;
    }
}
