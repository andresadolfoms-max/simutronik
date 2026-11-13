#include <avr/io.h>
#include <util/delay.h>
/* Dos LEDs alternados cada 500 ms en los pines D12 (PB4) y D13 (PB5). */
int main(void)
{
    DDRB = _BV(4) | _BV(5);
    for (;;)
    {
        PORTB = _BV(5);   /* D13 encendido, D12 apagado */
        _delay_ms(500);
        PORTB = _BV(4);   /* D12 encendido, D13 apagado */
        _delay_ms(500);
    }
}

