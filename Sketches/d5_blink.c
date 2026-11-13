#include <avr/io.h>
#include <util/delay.h>
/* D5 en ALTO fijo (la victima del corto) mientras D13 parpadea. */
int main(void)
{
    DDRD |= _BV(5);
    PORTD |= _BV(5);
    DDRB |= _BV(5);
    for (;;)
    {
        PORTB ^= _BV(5);
        _delay_ms(50);
    }
}
