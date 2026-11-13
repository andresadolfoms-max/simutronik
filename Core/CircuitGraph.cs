using ElectricSim.Core.Components;

namespace ElectricSim.Core;

/// <summary>
/// El controlador del circuito. Conoce todos los componentes activos y, en
/// cada Tick, resuelve los voltajes de toda la red en dos fases:
///
///   1. FUSIÓN: los cables y los interruptores cerrados ("conexiones
///      perfectas") fusionan sus dos pines en un solo nodo eléctrico real
///      (Union-Find), en vez de ser una resistencia casi-nula en el solver.
///   2. RELAJACIÓN: sobre esos nodos ya fusionados, los componentes que SÍ
///      tienen una resistencia real (resistencia, LED, capacitor...)
///      forman aristas, y cada nodo libre se actualiza, iteración a
///      iteración, como el promedio de sus vecinos ponderado por
///      conductancia (Ley de Ohm aplicada nodo a nodo).
///
/// Separar estas dos fases es lo que hace que el método converja en pocas
/// iteraciones — ver UnionFind.cs para el por qué.
///
/// Sobre esa base, cada tick hace además tres cosas nuevas:
///
///   a. CORTOCIRCUITOS: si al fusionar quedan en el mismo nodo dos pines
///      FIJOS con voltajes distintos (ej.: un cable directo del + al − de
///      una batería), no hay solución física posible — eso ES un corto. El
///      nodo toma el promedio y todas las fuentes involucradas reciben el
///      aviso (EnCortocircuito) para calentarse y morir como corresponde.
///
///   b. CLASIFICACIÓN AC/DC: se propaga por la red qué tipo de corriente
///      llega a cada componente (desde qué fuentes es alcanzable a través de
///      caminos que hoy conducen). Con eso cada componente sabe si le llega
///      DC, AC, una mezcla, o nada — y si es compatible con eso.
///
///   c. CORRIENTE DE FUENTES (Kirchhoff): la corriente real que entrega cada
///      fuente es la suma de las corrientes de las aristas resistivas
///      conectadas a su nodo positivo. Esto reemplaza el viejo "Current = 0
///      a propósito" de la v1 por el número físico de verdad.
/// </summary>
public class CircuitGraph
{
    public List<Component> Components { get; } = new();

    /// <summary>Cuántas veces se repite la relajación por tick. Con la fusión de cables, 15-20 alcanza de sobra.</summary>
    public int Iterations { get; set; } = 20;

    public void Agregar(Component componente)
    {
        Components.Add(componente);
        // Los compuestos (transistor, futuros transformadores) traen aristas
        // internas: entran al grafo como componentes comunes, invisibles
        // para el catálogo y la UI.
        if (componente is IComponenteCompuesto compuesto)
        {
            foreach (var arista in compuesto.AristasInternas)
            {
                Components.Add(arista);
            }
        }
    }

    public void Quitar(Component componente)
    {
        Components.Remove(componente);
        if (componente is IComponenteCompuesto compuesto)
        {
            foreach (var arista in compuesto.AristasInternas)
            {
                Components.Remove(arista);
            }
        }
    }

    /// <summary>Conecta dos pines con un cable (conexión perfecta). Devuelve el cable creado por si lo necesitás guardar.</summary>
    public Wire Conectar(Pin a, Pin b, string nombre = "Cable")
    {
        var cable = new Wire(a, b, nombre);
        Agregar(cable);
        return cable;
    }

    public void Tick(double deltaTime)
    {
        // Preparación por tick de los modelos compañeros: R_eq = dt/C en el
        // capacitor, L/dt en el inductor. Se hace UNA sola vez por tick, no
        // por iteración interna.
        foreach (var componente in Components)
        {
            if (componente is IComponenteConOffset conOffset)
            {
                conOffset.Preparar(deltaTime);
            }
        }

        // Estado que se reutiliza entre iteraciones internas.
        var pines = ObtenerPinesUnicos();
        var unionFind = new UnionFind();
        foreach (var pin in pines) unionFind.Asegurar(pin);

        foreach (var componente in Components)
        {
            if (componente is CajaConPantalla) continue;
            bool fusionado = componente.EsConexionPerfectaEfectiva();
            componente.FusionadoEsteTick = fusionado;
            if (!fusionado) componente.CorrienteFusionada = 0;
            if (fusionado) unionFind.Unir(componente.PinA, componente.PinB);
        }

        // La CajaConPantalla es una conexión de paso perfecta (entra por + y
        // sale por − exactamente igual), salvo que el usuario la conecte
        // directamente entre los dos bornes de una fuente sin ninguna carga en
        // serie: en ese caso se comporta como voltímetro abierto en vez de
        // cortocircuitar la fuente.
        foreach (var componente in Components)
        {
            if (componente is not CajaConPantalla caja) continue;
            if (!caja.EsConexionPerfectaEfectiva())
            {
                caja.FusionadoEsteTick = false;
                caja.CorrienteFusionada = 0;
                continue;
            }

            var repA = unionFind.Encontrar(caja.PinA);
            var repB = unionFind.Encontrar(caja.PinB);
            bool cortocircuitariaFuente = false;
            if (!ReferenceEquals(repA, repB))
            {
                foreach (var c in Components)
                {
                    if (c is FuenteBase f && !f.IsBurnt && f.EstaEncendida)
                    {
                        var fA = unionFind.Encontrar(f.PinA);
                        var fB = unionFind.Encontrar(f.PinB);
                        if (!ReferenceEquals(fA, fB) &&
                            ((ReferenceEquals(repA, fA) && ReferenceEquals(repB, fB)) ||
                             (ReferenceEquals(repA, fB) && ReferenceEquals(repB, fA))))
                        {
                            cortocircuitariaFuente = true;
                            break;
                        }
                    }
                }
            }

            caja.FusionadoEsteTick = !cortocircuitariaFuente;
            if (!caja.FusionadoEsteTick) caja.CorrienteFusionada = 0;
            else unionFind.Unir(caja.PinA, caja.PinB);
        }

        var representantes = pines.Select(unionFind.Encontrar).Distinct().ToList();

        var adyacencia = new Dictionary<Pin, List<(Pin vecino, Component porComponente)>>();
        foreach (var rep in representantes) adyacencia[rep] = new();
        foreach (var componente in Components)
        {
            if (componente.EsConexionPerfectaEfectiva()) continue;
            var repA = unionFind.Encontrar(componente.PinA);
            var repB = unionFind.Encontrar(componente.PinB);
            if (ReferenceEquals(repA, repB)) continue;
            adyacencia[repA].Add((repB, componente));
            adyacencia[repB].Add((repA, componente));
        }

        // La bandera de cortocircuito se evalúa UNA vez por tick: se limpia
        // ACÁ y las pasadas de convergencia solo pueden ENCENDERLA (OR).
        // Antes se reseteaba al inicio de cada pasada (en FijarFuentes), y
        // cuando el V_ef ya había colapsado a ~0 la última pasada no veía
        // conflicto y publicaba "sin corto" → la detección parpadeaba a ~3Hz
        // (bug QA2). Con el reset único + detección por voltajes NOMINALES
        // (ver ResolverGruposFijos), el estado queda clavado mientras el
        // cable del crimen siga puesto.
        foreach (var componente in Components)
        {
            if (componente is FuenteBase fb) fb.EnCortocircuito = false;
        }

        // CONVERGENCIA ITERATIVA DE V_EFECTIVO (Fix 4). Con cargas muy pesadas
        // (supercap descargado directo a pila botón, o carga que aparece de
        // golpe), V_ef arranca en V_nominal y la primera pasada del solver ve
        // una I gigante. Ajustamos V_ef con la fórmula del divisor de tensión
        // (que en un tick converge cerca del punto fijo) y volvemos a
        // resolver. Con 5 pasadas, V_ef, I y voltajes están estables.
        for (int iterConvergencia = 0; iterConvergencia < 5; iterConvergencia++)
        {
            FijarFuentes();

            // Nodos fijos + cortocircuitos, con el V_efectivo actual.
            var fijos = ResolverGruposFijos(pines, unionFind);
            ClasificarTiposDeCorriente(unionFind);

            SincronizarPines(pines, unionFind);
            for (int i = 0; i < Iterations; i++)
            {
                RelajarRepresentantes(representantes, fijos, adyacencia, unionFind);
                SincronizarPines(pines, unionFind);
            }

            CalcularCorrientesDeFuentes(unionFind);

            // Actualizar V_ef de cada fuente con la corriente recién medida.
            // Al terminar el loop, V_ef refleja el punto operativo real.
            foreach (var componente in Components)
            {
                if (componente is FuenteBase f && !f.IsBurnt)
                {
                    f.ActualizarVEfectivo();
                }
            }
        }

        // Corriente real de cada cable/interruptor fusionado (Kirchhoff
        // dentro de cada nodo fusionado — ver el método para el detalle).
        AsignarCorrientesDeFusionados(pines, unionFind);

        // Las redes de alterna con capacitores o inductores se resuelven con
        // fasores (sólo ellas: el resto del circuito no se toca).
        SolverAlterna.Resolver(Components, unionFind);

        ActualizarCajasConPantalla();

        foreach (var componente in Components)
        {
            componente.PostSolve(deltaTime);
        }
    }

    private void ActualizarCajasConPantalla()
    {
        double frecuenciaAc = 50.0;
        double voltajeFuenteActiva = 0.0;
        foreach (var componente in Components)
        {
            if (componente is FuenteBase f && !f.IsBurnt && f.EstaEncendida)
            {
                double vBornes = Math.Abs(f.PinA.Voltage - f.PinB.Voltage);
                if (vBornes <= 1e-4) vBornes = Math.Abs(f.VoltajeEfectivoParaSolver);
                if (vBornes > voltajeFuenteActiva) voltajeFuenteActiva = vBornes;
                if (f is FuenteAC fac) frecuenciaAc = fac.Frecuencia;
                else if (f is Inversor inv) frecuenciaAc = inv.FrecuenciaSalida;
            }
        }

        foreach (var componente in Components)
        {
            if (componente is not CajaConPantalla caja) continue;
            if (caja.ResultadoAC == null)
            {
                caja.FrecuenciaHz = frecuenciaAc;
            }

            if (caja.FusionadoEsteTick)
            {
                double vNodo = Math.Abs(caja.PinA.Voltage);
                if (vNodo <= 1e-4 && Math.Abs(caja.CorrienteFusionada) > 1e-6)
                {
                    vNodo = voltajeFuenteActiva;
                }
                caja.VoltajeNodoMedido = vNodo;
            }
            else
            {
                caja.VoltajeNodoMedido = Math.Abs(caja.PinA.Voltage - caja.PinB.Voltage);
            }
        }
    }

    private void FijarFuentes()
    {
        foreach (var componente in Components)
        {
            if (componente is FuenteBase fuenteBase && !fuenteBase.IsBurnt)
            {
                // Una fuente APAGADA (V≈0: transformador sin alimentar,
                // inversor en cutoff, regulador en protección) SUELTA sus
                // bornes: su salida es ALTA IMPEDANCIA, no un cable rígido
                // de 0V. Sin esto, colgar un convertidor apagado en paralelo
                // con la red aplastaba el riel entero a 0V sin aviso (QA13).
                // Al encenderse, vuelve a fijarlos al tick siguiente.
                bool encendida = fuenteBase.EstaEncendida;
                fuenteBase.PinA.IsFixed = encendida;
                fuenteBase.PinB.IsFixed = encendida;
                if (!encendida) continue;
            }

            if (componente is IVoltageSource fuente && !componente.IsBurnt)
            {
                fuente.FixVoltages();
            }
        }
    }

    /// <summary>
    /// Le da voltaje absoluto a cada nodo FIJO. Antes esto era "cada fuente
    /// estampa 0 y V absolutos, y los conflictos se promedian" — lo que hacía
    /// imposible poner fuentes EN SERIE (el − de la segunda batería no está a
    /// 0V absoluto: está donde lo dejó la primera). Ahora cada fuente impone
    /// una RESTRICCIÓN de diferencia, V(nodo+) = V(nodo−) + Voltaje, y acá se
    /// propagan con un punto fijo hasta que cada nodo queda coherente: dos 9V
    /// en serie dan 18V, como corresponde. Si dos caminos le exigen valores
    /// DISTINTOS al mismo nodo (el + y el − de una misma batería unidos por
    /// un cable, o dos fuentes de distinto voltaje en paralelo), eso sí es un
    /// CORTOCIRCUITO y se les avisa a las fuentes involucradas.
    ///
    /// Los grupos fijos SIN restricción de fuente (hoy: un capacitor
    /// entregando su carga, que fija sus propios bornes) conservan la regla
    /// vieja del promedio — y como no generan restricciones, jamás pueden
    /// marcar en falso a una batería.
    /// Devuelve el conjunto de nodos que la relajación no debe tocar.
    /// </summary>
    private HashSet<Pin> ResolverGruposFijos(List<Pin> pines, UnionFind unionFind)
    {
        // --- 1. Agrupar los pines fijos por nodo fusionado (como siempre) ---
        var grupos = new Dictionary<Pin, List<Pin>>();
        foreach (var pin in pines)
        {
            if (!pin.IsFixed) continue;
            var rep = unionFind.Encontrar(pin);
            if (!grupos.TryGetValue(rep, out var lista))
            {
                grupos[rep] = lista = new List<Pin>();
            }
            lista.Add(pin);
        }

        // --- 2. Juntar las restricciones de las fuentes vivas ---
        // OJO Fix 4: usamos VoltajeEfectivoParaSolver, NO Voltage. Bajo carga
        // el V_ef se hunde, y esa hundida se propaga por toda la cadena de
        // series, dando el 17V físico en dos 9V en serie con lámpara 60Ω.
        // Cada restricción lleva DOS deltas: el V_efectivo (con el que se
        // resuelven los voltajes reales de los nodos) y el V NOMINAL (con el
        // que se DETECTAN los conflictos). El nominal nunca colapsa, así que
        // la detección de cortos es estable aunque V_ef se haya ido a 0.
        var restricciones = new List<(Pin mas, Pin menos, double delta, double deltaNominal, FuenteBase dueno)>();
        var nodosQueSonMas = new HashSet<Pin>();
        foreach (var componente in Components)
        {
            if (componente is FuenteBase f && !f.IsBurnt && f.EstaEncendida)
            {
                var mas = unionFind.Encontrar(f.PinA);
                var menos = unionFind.Encontrar(f.PinB);
                restricciones.Add((mas, menos, f.VoltajeEfectivoParaSolver, f.Voltage, f));
                nodosQueSonMas.Add(mas);
            }
        }

        // --- 3. Punto fijo con SIEMBRA POSTERGADA (Fix 1) ---
        // Antes: en la primera vuelta se sembraba cualquier nodo − de una
        // fuente sin visitar → con tres pilas botón en serie, la del medio
        // sembraba SU OTRA punta a 0V mientras la de más abajo la exigía a
        // 3V, y el simulador declaraba corto donde no había ninguno.
        // Ahora: fase 1 sólo propaga por siembras NATURALES (nodos − que no
        // son + de nadie más — el "fondo" real de una cadena). Fase 2, sólo
        // si quedan restricciones cerradas sobre sí mismas (lazo puro de
        // fuentes), siembra UNA ciega y vuelve a propagar. Detecta los
        // cortocircuitos reales sin inventar falsos positivos.
        var voltajeDeNodo = new Dictionary<Pin, double>();
        var duenosDeNodo = new Dictionary<Pin, List<FuenteBase>>();

        void Anotar(Pin nodo, FuenteBase dueno)
        {
            if (!duenosDeNodo.TryGetValue(nodo, out var lista))
            {
                duenosDeNodo[nodo] = lista = new List<FuenteBase>();
            }
            if (!lista.Contains(dueno)) lista.Add(dueno);
        }
        void MarcarConflicto(Pin nodo, FuenteBase dueno)
        {
            dueno.EnCortocircuito = true;
            if (duenosDeNodo.TryGetValue(nodo, out var lista))
            {
                foreach (var f in lista) f.EnCortocircuito = true;
            }
        }
        foreach (var (mas, menos, _, _, dueno) in restricciones)
        {
            Anotar(mas, dueno);
            Anotar(menos, dueno);
        }

        // Sombra NOMINAL: los mismos nodos, propagados con los voltajes
        // nominales de las fuentes. Solo se usa para DETECTAR conflictos.
        var nominalDeNodo = new Dictionary<Pin, double>();

        bool Procesar((Pin mas, Pin menos, double delta, double deltaNominal, FuenteBase dueno) r, bool permitirCiega)
        {
            var (mas, menos, delta, deltaNominal, dueno) = r;
            if (ReferenceEquals(mas, menos))
            {
                // Lazo directo (+ y − fusionados en el mismo nodo): corto
                // estructural — se marca por el NOMINAL, no por el V_ef, así
                // no se "des-marca" cuando el V_ef colapsa a 0.
                if (Math.Abs(deltaNominal) > 0.05) dueno.EnCortocircuito = true;
                nominalDeNodo.TryAdd(mas, deltaNominal * 0.5);
                return voltajeDeNodo.TryAdd(mas, delta * 0.5);
            }
            bool hayMas = voltajeDeNodo.TryGetValue(mas, out double vMas);
            bool hayMenos = voltajeDeNodo.TryGetValue(menos, out double vMenos);
            if (!hayMas && !hayMenos)
            {
                // Siembra NATURAL: el nodo − es el "fondo" (no es + de
                // ninguna otra). Siembra CIEGA: cualquier nodo, sólo si la
                // fase 1 quedó estancada.
                if (!nodosQueSonMas.Contains(menos) || permitirCiega)
                {
                    voltajeDeNodo[menos] = 0;
                    voltajeDeNodo[mas] = delta;
                    nominalDeNodo[menos] = 0;
                    nominalDeNodo[mas] = deltaNominal;
                    return true;
                }
                return false;
            }
            if (hayMenos && !hayMas)
            {
                voltajeDeNodo[mas] = vMenos + delta;
                nominalDeNodo[mas] = nominalDeNodo.GetValueOrDefault(menos) + deltaNominal;
                return true;
            }
            if (hayMas && !hayMenos)
            {
                voltajeDeNodo[menos] = vMas - delta;
                nominalDeNodo[menos] = nominalDeNodo.GetValueOrDefault(mas) - deltaNominal;
                return true;
            }
            // Ambos nodos ya tienen valor: la restricción cierra un lazo.
            // El conflicto se juzga sobre los NOMINALES (bug QA2: en un corto
            // real el V_ef colapsa a ~0 y "0 ≈ 0" escondía el conflicto en la
            // última pasada — el nominal no colapsa nunca, la detección queda
            // clavada). Y con tolerancia RELATIVA (bug QA1: la vieja
            // tolerancia absoluta de 1µV marcaba en falso a dos fuentes
            // idénticas en paralelo por la asimetría numérica de sus V_ef).
            double nomMas = nominalDeNodo.GetValueOrDefault(mas);
            double nomMenos = nominalDeNodo.GetValueOrDefault(menos);
            double errorNominal = Math.Abs(nomMas - (nomMenos + deltaNominal));
            double tolerancia = Math.Max(0.05, 0.02 * Math.Abs(deltaNominal));
            if (errorNominal > tolerancia)
            {
                MarcarConflicto(mas, dueno);
                MarcarConflicto(menos, dueno);
            }
            return false;
        }

        // Fase 1: sólo siembras naturales, hasta que no cambie nada.
        int guardiaMax = restricciones.Count * 4 + 4;
        bool cambio = true;
        int guardia = 0;
        while (cambio && guardia++ < guardiaMax)
        {
            cambio = false;
            foreach (var r in restricciones)
            {
                if (Procesar(r, permitirCiega: false)) cambio = true;
            }
        }

        // Fase 2: si quedan restricciones sin cerrar, sembrar UNA ciega y
        // volver a propagar. Se repite hasta cubrir todo (lazos cerrados de
        // sólo fuentes, cada uno pide una siembra ciega).
        for (int intento = 0; intento < restricciones.Count + 1; intento++)
        {
            bool queda = false;
            foreach (var r in restricciones)
            {
                if (!voltajeDeNodo.ContainsKey(r.mas) || !voltajeDeNodo.ContainsKey(r.menos))
                {
                    queda = true;
                    break;
                }
            }
            if (!queda) break;
            // Sembrar UNA ciega para desatascar.
            foreach (var r in restricciones)
            {
                if (voltajeDeNodo.ContainsKey(r.mas) && voltajeDeNodo.ContainsKey(r.menos)) continue;
                Procesar(r, permitirCiega: true);
                break;
            }
            // Y propagar naturalmente lo que se pueda.
            cambio = true;
            guardia = 0;
            while (cambio && guardia++ < guardiaMax)
            {
                cambio = false;
                foreach (var r in restricciones)
                {
                    if (Procesar(r, permitirCiega: false)) cambio = true;
                }
            }
        }

        // --- 4. Volcar el resultado a los grupos fijos ---
        // Aplicamos v_nodo a TODOS los reps que quedaron con valor propagado
        // — no sólo a los grupos con pines fijos. Sin esto, un nodo
        // "intermedio" que recibe voltaje por una restricción propagada (por
        // ejemplo, el nodo del pin_b de un cap cuando el circuito le pone un
        // voltaje distinto de cero) se pisaba en la relajación un tick
        // después.
        var fijos = new HashSet<Pin>();
        foreach (var (repId, v) in voltajeDeNodo)
        {
            repId.Voltage = v;
            fijos.Add(repId);
        }
        foreach (var (rep, lista) in grupos)
        {
            if (fijos.Contains(rep)) continue; // ya se aplicó arriba
            // Grupo fijo sin restricción de fuente: promedio, como antes.
            rep.Voltage = lista.Average(p => p.Voltage);
            double min = lista.Min(p => p.Voltage);
            double max = lista.Max(p => p.Voltage);
            if (max - min > 1e-6)
            {
                foreach (var pin in lista)
                {
                    if (pin.Owner is FuenteBase fuente)
                    {
                        fuente.EnCortocircuito = true;
                    }
                }
            }
            fijos.Add(rep);
        }

        return fijos;
    }

    /// <summary>
    /// Decide qué tipo de corriente (bits: DC=1, AC=2) le llega a cada
    /// componente. La regla es POR CAMINO, no por isla conexa:
    ///
    ///   un componente recibe el tipo de una fuente si y solo si existe un
    ///   LAZO SIMPLE que pase por los dos.
    ///
    /// Antes esto era un OR de bits que se inundaba por cualquier arista que
    /// condujera, sin mirar la topología. El resultado era el bug 0.2: unir la
    /// masa de una batería con el neutro del tomacorriente —cosa normalísima
    /// en una mesa de trabajo— le ponía tipo=Mixta al LED y lo APAGABA, aunque
    /// no tocara la fuente de alterna por ningún lado y los voltajes de nodo
    /// siguieran siendo correctos. Le pasaba igual a capacitores, motores y
    /// optoacopladores. Que dos fuentes compartan masa no significa que sus
    /// corrientes se mezclen: en la vida real ese LED sigue prendido.
    ///
    /// "Estar en un lazo simple común" es exactamente "estar en la misma
    /// COMPONENTE BICONEXA" (ver CalcularBloques). Un nodo compartido es un
    /// punto de articulación y separa bloques, que es justo lo que hacía falta.
    ///
    /// Nota fina: usa el estado de conducción con los voltajes del tick
    /// ANTERIOR (todavía no relajamos). Para lo único que se usa es para la
    /// etiqueta AC/DC, y converge en un tick — es una simplificación
    /// consciente, no un bug.
    /// </summary>
    private void ClasificarTiposDeCorriente(UnionFind unionFind)
    {
        // ---- 1) grafo de aristas por las que PUEDE circular corriente ----
        // OJO: acá NO se usa ResistenciaParaSolver, que devuelve OpenResistance
        // si el componente es incompatible. Si lo usáramos, un motor DC en
        // alterna dejaría de ser arista → dejaría de recibir AC → dejaría de
        // ser incompatible → volvería a conducir. Oscila. Para CLASIFICAR
        // importa por dónde puede circular la corriente, no por dónde circula.
        var aristas = new List<(Pin A, Pin B, Component C)>();
        var adyacencia = new Dictionary<Pin, List<(Pin Otro, int Arista)>>();
        var indiceDe = new Dictionary<Component, int>();

        foreach (var componente in Components)
        {
            if (componente.EsConexionPerfectaEfectiva()) continue; // ya son el mismo nodo
            if (componente.IsBurnt) continue;
            if (componente.GetResistance() >= CircuitConstants.OpenResistance * 0.5) continue;

            var repA = unionFind.Encontrar(componente.PinA);
            var repB = unionFind.Encontrar(componente.PinB);
            if (ReferenceEquals(repA, repB)) continue;

            int idx = aristas.Count;
            aristas.Add((repA, repB, componente));
            indiceDe.TryAdd(componente, idx);
            if (!adyacencia.TryGetValue(repA, out var listaA)) adyacencia[repA] = listaA = new();
            if (!adyacencia.TryGetValue(repB, out var listaB)) adyacencia[repB] = listaB = new();
            listaA.Add((repB, idx));
            listaB.Add((repA, idx));
        }

        var bloqueDe = CalcularBloques(aristas, adyacencia);

        // ---- 2) cada fuente le pasa su tipo a SU bloque, y a nadie más ----
        var recibido = new Dictionary<Component, int>();
        void Marcar(Component c, int bits) =>
            recibido[c] = (recibido.TryGetValue(c, out var b) ? b : 0) | bits;

        foreach (var componente in Components)
        {
            int tipo;
            if (componente is FuenteBase fuente && !fuente.IsBurnt && fuente.EstaEntregandoTipo)
            {
                tipo = (int)fuente.Tipo;
            }
            else if (componente is Components.Capacitor cap && cap.EstaEntregando && !cap.IsBurnt)
            {
                // Un capacitor devolviendo su carga es una fuente DC de facto:
                // el LED que cuelga de él tiene que saber que le llega continua.
                tipo = (int)TipoCorriente.DC;
            }
            else continue;

            Marcar(componente, tipo);
            if (!indiceDe.TryGetValue(componente, out int propia)) continue;
            int miBloque = bloqueDe[propia];
            for (int i = 0; i < aristas.Count; i++)
            {
                if (bloqueDe[i] == miBloque) Marcar(aristas[i].C, tipo);
            }
        }

        // ---- 3) los que hoy están ABIERTOS igual tienen que ver a qué se
        // los enchufó (si no, la entrada de un convertidor no arranca nunca):
        // para ellos vale el tipo de los NODOS, como hacía el algoritmo viejo.
        var tipoNodo = new Dictionary<Pin, int>();
        for (int i = 0; i < aristas.Count; i++)
        {
            if (!recibido.TryGetValue(aristas[i].C, out int t) || t == 0) continue;
            tipoNodo[aristas[i].A] = (tipoNodo.TryGetValue(aristas[i].A, out var ta) ? ta : 0) | t;
            tipoNodo[aristas[i].B] = (tipoNodo.TryGetValue(aristas[i].B, out var tb) ? tb : 0) | t;
        }
        foreach (var componente in Components)
        {
            if (componente is FuenteBase fuente && !fuente.IsBurnt && fuente.EstaEntregandoTipo)
            {
                int t = (int)fuente.Tipo;
                tipoNodo.TryAdd(unionFind.Encontrar(fuente.PinA), t);
                tipoNodo.TryAdd(unionFind.Encontrar(fuente.PinB), t);
            }
        }

        foreach (var componente in Components)
        {
            int bits;
            if (recibido.TryGetValue(componente, out int r)) bits = r;
            else if (!indiceDe.ContainsKey(componente))
            {
                int ta = tipoNodo.TryGetValue(unionFind.Encontrar(componente.PinA), out var x) ? x : 0;
                int tb = tipoNodo.TryGetValue(unionFind.Encontrar(componente.PinB), out var y) ? y : 0;
                bits = ta | tb;
            }
            else bits = 0;

            componente.TipoCorrienteRecibida = (TipoCorriente)bits;
        }
    }

    /// <summary>
    /// Componentes BICONEXAS (bloques de Tarjan), iterativo. Dos aristas están
    /// en el mismo bloque si y solo si existe un LAZO SIMPLE que pase por las
    /// dos — que es exactamente la condición para que la corriente de una
    /// pueda circular por la otra.
    ///
    /// Es LO que arregla el bug 0.2: compartir un nodo (masa común) no alcanza,
    /// porque un nodo compartido es un punto de articulación y separa bloques.
    /// Devuelve, por índice de arista, el id de su bloque.
    /// </summary>
    private static int[] CalcularBloques(
        List<(Pin A, Pin B, Component C)> aristas,
        Dictionary<Pin, List<(Pin Otro, int Arista)>> adyacencia)
    {
        var bloqueDe = new int[aristas.Count];
        for (int i = 0; i < bloqueDe.Length; i++) bloqueDe[i] = -1;

        var descubierto = new Dictionary<Pin, int>();
        var bajo = new Dictionary<Pin, int>();
        var pilaAristas = new Stack<int>();
        int reloj = 0, bloques = 0;

        foreach (var raiz in adyacencia.Keys)
        {
            if (descubierto.ContainsKey(raiz)) continue;

            descubierto[raiz] = bajo[raiz] = reloj++;
            var pila = new Stack<(Pin Nodo, int AristaPadre, IEnumerator<(Pin Otro, int Arista)> It)>();
            pila.Push((raiz, -1, adyacencia[raiz].GetEnumerator()));

            while (pila.Count > 0)
            {
                var (nodo, aristaPadre, it) = pila.Peek();
                bool descendio = false;

                while (it.MoveNext())
                {
                    var (otro, iAr) = it.Current;
                    if (iAr == aristaPadre) continue;

                    if (!descubierto.ContainsKey(otro))
                    {
                        pilaAristas.Push(iAr);
                        descubierto[otro] = bajo[otro] = reloj++;
                        pila.Push((otro, iAr, adyacencia[otro].GetEnumerator()));
                        descendio = true;
                        break;
                    }
                    if (descubierto[otro] < descubierto[nodo])
                    {
                        pilaAristas.Push(iAr);
                        bajo[nodo] = Math.Min(bajo[nodo], descubierto[otro]);
                    }
                }
                if (descendio) continue;

                pila.Pop();
                if (pila.Count == 0) continue;

                var padre = pila.Peek().Nodo;
                bajo[padre] = Math.Min(bajo[padre], bajo[nodo]);
                if (bajo[nodo] >= descubierto[padre])
                {
                    // el padre es punto de articulación: se cierra un bloque
                    bloques++;
                    while (pilaAristas.Count > 0)
                    {
                        int i = pilaAristas.Pop();
                        bloqueDe[i] = bloques;
                        if (i == aristaPadre) break;
                    }
                }
            }
        }

        for (int i = 0; i < bloqueDe.Length; i++)
        {
            if (bloqueDe[i] < 0) bloqueDe[i] = ++bloques; // arista suelta: bloque propio
        }
        return bloqueDe;
    }

    /// <summary>
    /// Ley de Kirchhoff de corrientes en el ÁRBOL DE FUENTES (Fix 2). La v3
    /// vieja miraba sólo el balance del nodo + de cada fuente, y eso rompía
    /// en cadenas: en dos 9V en serie con lámpara 60Ω, la fuente del medio
    /// veía "salida = 0" (la del arriba manda su 300mA al medio, y la del
    /// medio manda sus 300mA al abajo, se cancelan) y quedaba con Current=0
    /// aunque físicamente lleva la MISMA corriente que las demás.
    ///
    /// Ahora: construimos un GRAFO DE FUENTES donde cada fuente viva es una
    /// arista dirigida entre su nodo + y su −. Los capacitores YA NO
    /// participan acá (son aristas resistivas normales con offset, ver
    /// RelajarRepresentantes y su Ohm generalizado). BFS por componentes
    /// conexos, y en cada uno acumulamos las inyecciones RESISTIVAS externas
    /// hoja→raíz: la corriente de cada fuente es lo que hace falta para
    /// cerrar Kirchhoff nodo por nodo. Resultado: TODAS las fuentes en serie
    /// muestran la misma corriente con el mismo signo, como corresponde.
    /// </summary>
    private void CalcularCorrientesDeFuentes(UnionFind unionFind)
    {
        List<FuenteBase>? fuentes = null;
        foreach (var componente in Components)
        {
            if (componente is FuenteBase f && !f.IsBurnt && f.EstaEncendida)
            {
                (fuentes ??= new List<FuenteBase>()).Add(f);
            }
            else if (componente is FuenteBase apagada)
            {
                apagada.CorrienteEntregada = 0; // apagada o quemada: alta impedancia, no entrega nada
            }
        }
        if (fuentes == null) return;

        // Paso 1: inyección neta de las aristas RESISTIVAS en cada nodo. Los
        // capacitores participan aquí también (son aristas resistivas con
        // Ohm generalizado: I = (ΔV − V_alm) / R_eq).
        var inyeccion = new Dictionary<Pin, double>();
        void Inyectar(Pin nodo, double amps)
        {
            inyeccion[nodo] = inyeccion.GetValueOrDefault(nodo) + amps;
        }
        foreach (var componente in Components)
        {
            if (componente is FuenteBase) continue;
            if (componente.IsBurnt || componente.EsConexionPerfectaEfectiva()) continue;
            var repA = unionFind.Encontrar(componente.PinA);
            var repB = unionFind.Encontrar(componente.PinB);
            if (ReferenceEquals(repA, repB)) continue;

            double offset = componente is IComponenteConOffset conOffset ? conOffset.OffsetVoltaje : 0.0;
            double corriente = LeyesElectricas.Corriente(
                repA.Voltage - repB.Voltage - offset, componente.ResistenciaParaSolver());
            Inyectar(repA, +corriente);  // sale del nodo A → aporte + al balance "sale"
            Inyectar(repB, -corriente);  // entra al nodo B → aporte − al balance "sale"
        }

        // Paso 2: aristas del árbol de fuentes. Por cada fuente viva, arista
        // dirigida entre su + (mas) y su − (menos). aristasPorNodo lista las
        // aristas de CADA nodo con un flag +1/−1: +1 si mas está en ESTE
        // nodo, −1 si mas está del otro lado.
        // Fix QA1: fuentes PARALELAS entre el mismo par de nodos (misma
        // orientación) se agrupan en UNA sola arista representante. Antes,
        // la segunda formaba un ciclo en el árbol y quedaba sin corriente
        // asignada (0A): una batería cargaba con todo, la otra con nada, sus
        // V_ef divergían y el falso conflicto las mandaba a las dos al corto.
        // Después del árbol, la corriente del grupo se reparte por
        // conductancia interna (más abajo).
        var aristasPorNodo = new Dictionary<Pin, List<(Pin otro, FuenteBase comp, int dir)>>();
        var aristasFuente = new List<(Pin mas, Pin menos, FuenteBase comp)>();
        var grupoDeParalelas = new Dictionary<(Pin, Pin), List<FuenteBase>>();
        foreach (var f in fuentes)
        {
            var mas = unionFind.Encontrar(f.PinA);
            var menos = unionFind.Encontrar(f.PinB);
            if (ReferenceEquals(mas, menos)) continue;
            if (grupoDeParalelas.TryGetValue((mas, menos), out var paralelas))
            {
                paralelas.Add(f); // paralela de una ya vista: no entra al árbol
                continue;
            }
            grupoDeParalelas[(mas, menos)] = new List<FuenteBase> { f };
            aristasFuente.Add((mas, menos, f));
            if (!aristasPorNodo.TryGetValue(mas, out var la)) aristasPorNodo[mas] = la = new();
            la.Add((menos, f, +1));
            if (!aristasPorNodo.TryGetValue(menos, out var lb)) aristasPorNodo[menos] = lb = new();
            lb.Add((mas, f, -1));
        }

        // Paso 3: BFS por componentes conexos, hoja→raíz. En cada nodo hijo,
        // la corriente de la arista que lo conecta al padre debe balancear el
        // "sale" acumulado del hijo (sus inyecciones resistivas + las
        // corrientes ya asignadas de sus otros hijos).
        var visitado = new HashSet<Pin>();
        var padre = new Dictionary<Pin, (Pin pinPadre, FuenteBase comp, int dirEnHijo)>();
        var corrAsignada = new Dictionary<FuenteBase, double>();

        foreach (var arista in aristasFuente)
        {
            var raiz = visitado.Contains(arista.mas) ? arista.menos : arista.mas;
            if (visitado.Contains(raiz)) continue;
            visitado.Add(raiz);
            var orden = new List<Pin> { raiz };
            for (int i = 0; i < orden.Count; i++)
            {
                var nodo = orden[i];
                if (!aristasPorNodo.TryGetValue(nodo, out var vecinos)) continue;
                foreach (var (otro, comp, dirDesdePadre) in vecinos)
                {
                    if (visitado.Contains(otro)) continue;
                    if (corrAsignada.ContainsKey(comp)) continue;
                    visitado.Add(otro);
                    // dirDesdePadre viene de aristasPorNodo[padre]: +1 si mas
                    // está en el padre, −1 si mas está en el hijo. Al guardar
                    // padre[hijo], INVERTIMOS: dirEnHijo = +1 si mas está en
                    // el hijo, −1 si mas está en el padre.
                    padre[otro] = (nodo, comp, -dirDesdePadre);
                    orden.Add(otro);
                }
            }

            // Acumulador arranca con las inyecciones resistivas de cada nodo.
            var acum = new Dictionary<Pin, double>();
            foreach (var p in orden) acum[p] = inyeccion.GetValueOrDefault(p);

            for (int k = orden.Count - 1; k > 0; k--)
            {
                var nodo = orden[k];
                var (nodoPadre, comp, dirEnHijo) = padre[nodo];
                // Balance en el hijo: acum + aporte_fuente_al_sale = 0.
                // dirEnHijo = +1 (mas en hijo): fuente empuja hacia el hijo
                //   desde adentro → aporta −corr al sale del hijo →
                //   acum + (−corr) = 0 → corr = acum. Y en el padre, la
                //   fuente aporta +corr → acum[padre] += corr.
                // dirEnHijo = −1 (menos en hijo): fuente absorbe del hijo →
                //   aporta +corr → corr = −acum. En el padre, aporta −corr →
                //   acum[padre] -= corr.
                double corr;
                if (dirEnHijo == +1)
                {
                    corr = acum[nodo];
                    acum[nodoPadre] += corr;
                }
                else
                {
                    corr = -acum[nodo];
                    acum[nodoPadre] -= corr;
                }
                corrAsignada[comp] = corr;
            }
        }

        // Reparto de los grupos paralelos: la corriente total que el árbol le
        // asignó al representante se divide entre las fuentes del grupo según
        // su CONDUCTANCIA interna (1/R_int). Idénticas → mitades exactas;
        // misma V con distinta R_int → el reparto físico real, que además
        // deja los V_ef iguales (V − I·R idéntico) y evita todo conflicto.
        foreach (var (_, paralelas) in grupoDeParalelas)
        {
            if (paralelas.Count < 2) continue;
            double total = corrAsignada.GetValueOrDefault(paralelas[0]);
            double sumaConductancia = 0;
            foreach (var f in paralelas)
            {
                sumaConductancia += 1.0 / Math.Max(f.ResistenciaInternaNominal, 1e-3);
            }
            foreach (var f in paralelas)
            {
                double conductancia = 1.0 / Math.Max(f.ResistenciaInternaNominal, 1e-3);
                corrAsignada[f] = total * conductancia / sumaConductancia;
            }
        }

        foreach (var f in fuentes)
        {
            // Una fuente EN CORTO no tiene rama externa por donde medirle la
            // corriente con Kirchhoff: sus dos bornes cayeron en el mismo grupo
            // fusionado, así que el árbol de arriba la saltea y le queda 0. Su
            // corriente es la de CORTOCIRCUITO. Esto ya se calculaba, pero
            // recién en FuenteBase.PostSolve — o sea DESPUÉS del reparto de
            // corrientes fusionadas, que por eso le asignaba 0 A al cable que
            // llevaba los 240 A y lo dejaba a temperatura ambiente (B8/A9).
            f.CorrienteEntregada = f.EnCortocircuito
                ? Math.Abs(f.Voltage) / Math.Max(f.ResistenciaInternaNominal, 0.05)
                : corrAsignada.GetValueOrDefault(f);
        }
    }

    /// <summary>
    /// Asigna la corriente REAL que atraviesa cada componente fusionado
    /// (cables, interruptores cerrados, pulsadores presionados). Sin esto,
    /// el inspector mostraba "0.0 mA" en un cable que llevaba 90 mA — y un
    /// cable/interruptor con límite de corriente editado no se quemaba nunca.
    ///
    /// CÓMO: dentro de cada nodo fusionado, los pines originales siguen
    /// existiendo — la fusión es eléctrica, no geométrica. Por cada pin
    /// sabemos cuánta corriente le INYECTA el exterior (las aristas
    /// resistivas y las fuentes que tocan exactamente ese pin). Con esas
    /// inyecciones, la corriente por cada conexión perfecta interna sale por
    /// Ley de Kirchhoff: armamos un ÁRBOL de expansión del grupo (BFS) y la
    /// corriente de cada rama es la suma de las inyecciones del subárbol que
    /// cuelga de ella (lo que entra por un lado tiene que salir por el otro).
    ///
    /// LIMITACIÓN CONOCIDA (documentada a propósito): si hay cables ideales
    /// REDUNDANTES en paralelo (dos cables perfectos uniendo los mismos dos
    /// puntos), el reparto entre ellos es físicamente indeterminado — en la
    /// realidad depende de sus resistencias minúsculas. Acá toda la corriente
    /// se asigna al que quedó en el árbol y el redundante marca 0; la SUMA
    /// siempre es la correcta (hay un test que lo vigila).
    /// </summary>
    private void AsignarCorrientesDeFusionados(List<Pin> pines, UnionFind unionFind)
    {
        // --- 1. Inyección externa de corriente en cada PIN EXACTO ---
        var inyeccion = new Dictionary<Pin, double>();
        void Inyectar(Pin pin, double amps)
        {
            inyeccion[pin] = inyeccion.GetValueOrDefault(pin) + amps;
        }

        foreach (var componente in Components)
        {
            if (componente.FusionadoEsteTick || componente is CajaConPantalla) continue; // las internas se resuelven después

            if (componente is FuenteBase fuente)
            {
                if (fuente.IsBurnt) continue;
                // La fuente EMPUJA su corriente al nodo del borne positivo y
                // la RECIBE de vuelta por el negativo (el lazo se cierra por
                // adentro de la fuente).
                Inyectar(fuente.PinA, fuente.CorrienteEntregada);
                Inyectar(fuente.PinB, -fuente.CorrienteEntregada);
                continue;
            }

            var repA = unionFind.Encontrar(componente.PinA);
            var repB = unionFind.Encontrar(componente.PinB);
            if (ReferenceEquals(repA, repB)) continue; // arista interna a un grupo: ΔV=0, no inyecta

            // Corriente positiva = fluye de A hacia B por el componente:
            // SALE del grupo de A (inyección negativa ahí) y ENTRA al de B.
            // El cap participa acá con Ohm GENERALIZADO: I = (ΔV − V_alm)/R_eq.
            double offset = componente is IComponenteConOffset conOffset ? conOffset.OffsetVoltaje : 0.0;
            double corriente = LeyesElectricas.Corriente(
                repA.Voltage - repB.Voltage - offset, componente.ResistenciaParaSolver());
            Inyectar(componente.PinA, -corriente);
            Inyectar(componente.PinB, corriente);
        }

        // --- 2. Agrupar los pines por nodo fusionado y las aristas perfectas ---
        var miembros = new Dictionary<Pin, List<Pin>>();          // representante -> pines del grupo
        foreach (var pin in pines)
        {
            var rep = unionFind.Encontrar(pin);
            if (!miembros.TryGetValue(rep, out var lista)) miembros[rep] = lista = new();
            lista.Add(pin);
        }

        var aristasPorPin = new Dictionary<Pin, List<(Pin otro, Component porComponente)>>();
        foreach (var componente in Components)
        {
            if (!componente.FusionadoEsteTick) continue;
            if (ReferenceEquals(componente.PinA, componente.PinB))
            {
                componente.CorrienteFusionada = 0; // lazo a sí mismo: corriente 0
                continue;
            }

            if (!aristasPorPin.TryGetValue(componente.PinA, out var la)) aristasPorPin[componente.PinA] = la = new();
            la.Add((componente.PinB, componente));
            if (!aristasPorPin.TryGetValue(componente.PinB, out var lb)) aristasPorPin[componente.PinB] = lb = new();
            lb.Add((componente.PinA, componente));
        }

        if (aristasPorPin.Count == 0) return; // no hay nada fusionado este tick

        // --- 3. Por cada grupo: árbol BFS y corrientes de las ramas ---
        foreach (var (_, grupo) in miembros)
        {
            if (grupo.Count < 2) continue;

            // BFS desde el primer pin del grupo, registrando por qué
            // componente llegamos a cada pin (esa es la rama del árbol).
            var padre = new Dictionary<Pin, (Pin pin, Component rama)?> { [grupo[0]] = null };
            var orden = new List<Pin> { grupo[0] };
            var enArbol = new HashSet<Component>();
            for (int i = 0; i < orden.Count; i++)
            {
                if (!aristasPorPin.TryGetValue(orden[i], out var vecinos)) continue;
                foreach (var (otro, porComponente) in vecinos)
                {
                    if (padre.ContainsKey(otro)) continue;
                    padre[otro] = (orden[i], porComponente);
                    enArbol.Add(porComponente);
                    orden.Add(otro);
                }
            }

            // De las hojas hacia la raíz: lo que se inyecta en todo el
            // subárbol de un pin es lo que tiene que viajar por la rama que
            // lo une con su padre.
            var acumulado = new Dictionary<Pin, double>();
            foreach (var pin in orden)
            {
                acumulado[pin] = inyeccion.GetValueOrDefault(pin);
            }
            for (int i = orden.Count - 1; i > 0; i--)
            {
                var pin = orden[i];
                var (pinPadre, rama) = padre[pin]!.Value;

                double flujoHaciaPadre = acumulado[pin]; // Kirchhoff: entra al subárbol → sale hacia el padre
                acumulado[pinPadre] += flujoHaciaPadre;

                // Signo con la convención del componente: Current positiva
                // = fluye de SU PinA hacia SU PinB.
                rama.CorrienteFusionada = ReferenceEquals(rama.PinA, pin)
                    ? flujoHaciaPadre
                    : -flujoHaciaPadre;
            }

            // Las conexiones perfectas redundantes (cerraban un ciclo dentro
            // del grupo y no entraron al árbol) quedan con corriente 0 — el
            // reparto real entre cables ideales en paralelo es indeterminado.
            foreach (var pin in grupo)
            {
                if (!aristasPorPin.TryGetValue(pin, out var vecinos)) continue;
                foreach (var (_, porComponente) in vecinos)
                {
                    if (!enArbol.Contains(porComponente))
                    {
                        porComponente.CorrienteFusionada = 0;
                    }
                }
            }
        }
    }

    private List<Pin> ObtenerPinesUnicos()
    {
        var vistos = new HashSet<Pin>();
        var lista = new List<Pin>();
        foreach (var componente in Components)
        {
            if (vistos.Add(componente.PinA)) lista.Add(componente.PinA);
            if (vistos.Add(componente.PinB)) lista.Add(componente.PinB);
        }
        return lista;
    }

    private void SincronizarPines(List<Pin> pines, UnionFind unionFind)
    {
        foreach (var pin in pines)
        {
            var rep = unionFind.Encontrar(pin);
            if (!ReferenceEquals(pin, rep))
            {
                pin.Voltage = rep.Voltage;
            }
        }
    }

    /// <summary>
    /// Un paso de relajación: el nuevo voltaje de cada nodo libre es el
    /// promedio de sus vecinos ponderado por conductancia (Ley de Ohm: a
    /// menor resistencia de un vecino, más "tira" ese vecino del resultado).
    ///
    /// Fix 3 — OFFSET del cap: cuando la arista que une con el vecino es un
    /// Capacitor, su fem interna V_alm se suma al voltaje del vecino con el
    /// signo que corresponde: V(PinA) − V(PinB) = V_alm + I·R_eq, así que al
    /// calcular V(PinA) el vecino PinB aporta (V + V_alm), y al calcular
    /// V(PinB) el vecino PinA aporta (V − V_alm). Sin esto, el cap con
    /// polaridad invertida no cierra Kirchhoff y v_alm crece sin límite.
    /// </summary>
    private void RelajarRepresentantes(
        List<Pin> representantes,
        HashSet<Pin> fijos,
        Dictionary<Pin, List<(Pin vecino, Component porComponente)>> adyacencia,
        UnionFind unionFind)
    {
        foreach (var rep in representantes)
        {
            if (fijos.Contains(rep)) continue;

            double numerador = 0;
            double sumaConductancias = 0;

            foreach (var (vecino, componente) in adyacencia[rep])
            {
                double conductancia = LeyesElectricas.Conductancia(componente.ResistenciaParaSolver());
                double offset = 0.0;
                if (componente is IComponenteConOffset conOffset)
                {
                    // ¿rep está del lado de PinA o PinB del componente?
                    bool repEsLadoA = ReferenceEquals(unionFind.Encontrar(componente.PinA), rep);
                    offset = repEsLadoA ? +conOffset.OffsetVoltaje : -conOffset.OffsetVoltaje;
                }
                numerador += (vecino.Voltage + offset) * conductancia;
                sumaConductancias += conductancia;
            }

            if (sumaConductancias > 0)
            {
                rep.Voltage = numerador / sumaConductancias;
            }
        }
    }
}
