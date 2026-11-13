#include <avr/io.h>
static void putc_(char c)
{
    while (!(UCSR0A & _BV(UDRE0)));
    UDR0 = (unsigned char)c;
}
static void print_(const char *s) { while (*s) putc_(*s++); }
int main(void)
{
    UBRR0L = 103;                          /* 9600 baudios */
    UCSR0B = _BV(RXEN0) | _BV(TXEN0);
    UCSR0C = _BV(UCSZ01) | _BV(UCSZ00);
    print_("hola desde el chip\n");
    for (;;)
    {
        if (UCSR0A & _BV(RXC0))            /* eco: lo que llega, vuelve */
        {
            unsigned char c = UDR0;
            putc_((char)c);
        }
    }
}
