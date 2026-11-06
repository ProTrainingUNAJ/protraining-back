namespace Application.Exceptions;

// Entrenamiento no respondió (caída, timeout o circuit breaker abierto tras los
// reintentos de Polly). El middleware la traduce a 503: la confirmación es síncrona
// y el PF debe ver que el 1RM no se sincronizó (KAN-426).
public sealed class EntrenamientoUnavailableException(Exception? innerException = null)
    : Exception("El servicio de Entrenamiento no está disponible.", innerException);
