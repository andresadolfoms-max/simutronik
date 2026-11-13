#include <avr/io.h>
#include <avr/pgmspace.h>

/* Tortura de la ISA: cada chequeo enciende un bit de PORTD/PORTB.
   Si el emulador ejecuta bien TODO, los 14 pines quedan en ALTO. */

const unsigned char tabla[] PROGMEM = {3, 1, 4, 1, 5, 9, 2, 6};

volatile unsigned char v8a = 200, v8b = 57;
volatile unsigned int v16a = 51234, v16b = 14302;
volatile long v32 = -123456L;
volatile unsigned char idx = 5;

unsigned char suma_rec(unsigned char n)
{
    return n == 0 ? 0 : (unsigned char)(n + suma_rec((unsigned char)(n - 1)));
}

int main(void)
{
    unsigned char ok = 0, okb = 0;
    DDRD = 0xFF;
    DDRB = 0x3F;

    if ((unsigned char)(v8a + v8b) == 1) ok |= 1;            /* ADD de 8 bits que envuelve */
    if ((unsigned int)(v16a + v16b) == 0) ok |= 2;           /* ADD/ADC de 16 bits (65536 -> 0) */
    if ((unsigned int)(v16a - v16b) == 36932u) ok |= 4;      /* SUB/SBC */
    if (v8a * v8b == 11400) ok |= 8;                         /* MUL (200*57) */
    if ((v32 >> 3) == -15432L) ok |= 16;                     /* ASR de 32 bits */
    if (((signed char)v8a) < 0 && v8b > 0) ok |= 32;         /* comparacion con signo (200 -> -56) */
    if (pgm_read_byte(&tabla[idx]) == 9) ok |= 64;           /* LPM */
    if (suma_rec(10) == 55) ok |= 128;                       /* CALL/RET/pila (recursion) */
    PORTD = ok;

    {
        volatile unsigned char arr[5];
        unsigned char i;
        for (i = 0; i < 5; i++) arr[i] = (unsigned char)(i * i);
        if (arr[4] == 16 && arr[3] == 9) okb |= 1;           /* ST/LDD con desplazamiento */
    }
    {
        unsigned char x = 0xA5;
        x = (unsigned char)((x << 4) | (x >> 4));
        if (x == 0x5A) okb |= 2;                             /* SWAP */
    }
    v8a = 100; v8b = 100;
    if (v8a == v8b) okb |= 4;                                /* CP/BREQ */
    okb |= 8;                                                /* llego vivo hasta aca */
    PORTB = (unsigned char)(okb | 0x30);
    for (;;);
}
