#include <avr/io.h>
#include <util/delay.h>
/* Parpadeo lento (500 ms encendido / 500 ms apagado) en el pin D13 (PB5).
   Frecuencia base del ATmega328P: 16 MHz. */
int main(void)
{
    DDRB |= _BV(5);
    for (;;)
    {
        PORTB |= _BV(5);
        _delay_ms(500);
        PORTB &= ~_BV(5);
        _delay_ms(500);
    }
}

