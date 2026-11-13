#include <avr/io.h>
static void putc_(char c)
{
    while (!(UCSR0A & _BV(UDRE0)));
    UDR0 = (unsigned char)c;
}
static void printnum(unsigned int v)
{
    char b[6];
    unsigned char i = 0;
    if (!v) putc_('0');
    while (v) { b[i++] = (char)('0' + v % 10); v /= 10; }
    while (i) putc_(b[--i]);
    putc_('\n');
}
int main(void)
{
    UBRR0L = 103;
    UCSR0B = _BV(TXEN0);
    UCSR0C = _BV(UCSZ01) | _BV(UCSZ00);
    ADMUX = _BV(REFS0);                              /* AVcc, canal A0 */
    ADCSRA = _BV(ADEN) | _BV(ADPS2) | _BV(ADPS1) | _BV(ADPS0);
    for (;;)
    {
        ADCSRA |= _BV(ADSC);
        while (ADCSRA & _BV(ADSC));
        unsigned int v = ADCL;
        v |= (unsigned int)ADCH << 8;
        printnum(v);
        for (volatile unsigned int k = 0; k < 2000; k++);
    }
}
