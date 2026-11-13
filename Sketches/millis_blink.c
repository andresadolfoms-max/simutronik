#include <avr/io.h>
#include <avr/interrupt.h>
/* Replica fiel del corazon de wiring.c del core de Arduino:
   Timer0 en fast PWM con prescaler 64, interrupcion de desborde cada
   1.024ms, millis()/micros()/delay() identicos a los reales. */

volatile unsigned long timer0_overflow_count = 0;
volatile unsigned long timer0_millis = 0;
static unsigned char timer0_fract = 0;

ISR(TIMER0_OVF_vect)
{
    unsigned long m = timer0_millis;
    unsigned char f = timer0_fract;
    m += 1;
    f += 3;
    if (f >= 125) { f -= 125; m += 1; }
    timer0_fract = f;
    timer0_millis = m;
    timer0_overflow_count++;
}

unsigned long millis(void)
{
    unsigned long m;
    unsigned char oldSREG = SREG;
    cli();
    m = timer0_millis;
    SREG = oldSREG;
    return m;
}

unsigned long micros(void)
{
    unsigned long m;
    unsigned char oldSREG = SREG, t;
    cli();
    m = timer0_overflow_count;
    t = TCNT0;
    if ((TIFR0 & _BV(TOV0)) && (t < 255)) m++;
    SREG = oldSREG;
    return ((m << 8) + t) * (64 / (F_CPU / 1000000L));
}

void delay(unsigned long ms)
{
    unsigned long start = micros();
    while (ms > 0)
    {
        while (ms > 0 && (micros() - start) >= 1000)
        {
            ms--;
            start += 1000;
        }
    }
}

int main(void)
{
    TCCR0A = 3;          /* fast PWM, como el init() real */
    TCCR0B = 3;          /* prescaler 64 */
    TIMSK0 = 1;          /* interrupcion de desborde */
    sei();
    DDRB |= _BV(5);
    for (;;)
    {
        PORTB ^= _BV(5);
        delay(50);
    }
}
