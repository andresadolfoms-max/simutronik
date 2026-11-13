using System.Runtime.CompilerServices;

// La suite de tests necesita tocar miembros `internal` del Core (por ejemplo
// Capacitor.VoltajeAlmacenado, que tiene `internal set`) para armar escenarios
// con el capacitor pre-cargado. Sin esto, Tests/Program.cs NO COMPILA — era el
// primer hallazgo de la auditoría QA: el proyecto venía con la red de
// regresión rota de entrada.
[assembly: InternalsVisibleTo("Tests")]
