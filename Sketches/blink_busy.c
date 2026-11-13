#include <avr/io.h>
#include <util/delay.h>
/* El blink clasico por espera activa: _delay_ms cuenta ciclos a mano,
   asi que el periodo depende de que CADA instruccion dure lo que debe. */
int main(void)
{
    DDRB |= _BV(5);
    for (;;)
    {
        PORTB |= _BV(5);
        _delay_ms(100);
        PORTB &= ~_BV(5);
        _delay_ms(100);
    }
}
