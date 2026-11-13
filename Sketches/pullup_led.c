#include <avr/io.h>
/* D2 como entrada con pull-up, espejado (invertido) al LED de D13:
   boton a GND apretado -> LED prendido. */
int main(void)
{
    DDRD &= (unsigned char)~_BV(2);
    PORTD |= _BV(2);
    DDRB |= _BV(5);
    for (;;)
    {
        if (PIND & _BV(2)) PORTB &= (unsigned char)~_BV(5);
        else PORTB |= _BV(5);
    }
}
