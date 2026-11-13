#include <avr/io.h>
#include <util/delay.h>
/* Semáforo de 3 LEDs:
   - D11 (PB3) encendido 1000 ms (ej. LED verde)
   - D12 (PB4) encendido  500 ms (ej. LED amarillo)
   - D13 (PB5) encendido 1000 ms (ej. LED rojo + LED "L" de la placa) */
int main(void)
{
    DDRB = _BV(3) | _BV(4) | _BV(5);
    for (;;)
    {
        PORTB = _BV(3);
        _delay_ms(1000);
        PORTB = _BV(4);
        _delay_ms(500);
        PORTB = _BV(5);
        _delay_ms(1000);
    }
}

