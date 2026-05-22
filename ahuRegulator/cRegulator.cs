using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ahuKlasy;

namespace ahuRegulator
{
    #region ParametryLokalne
    class cRegulatorPI
    {
        double Ts = 1;
        public double calka = 0;

        public double kp = 1;
        public double ki = 0;

        // ograniczenia wysterowania
        public double ymin;
        public double ymax;

        public double Wyjscie(double Uchyb)
        {
            // Zabezpieczenie przed wartościami nieokreślonymi
            if (double.IsNaN(Uchyb)) Uchyb = 0;

            double y = kp * Uchyb + ki * calka;  //wyjście regulatora

            //anti wind-up
            if (y < ymax && y > ymin)
            {
                calka = calka + Uchyb * Ts;
            }

            if (double.IsNaN(calka)) calka = 0;

            y = kp * Uchyb + ki * calka;

            //nasycenie
            if (y > ymax) y = ymax;
            if (y < ymin) y = ymin;

            if (double.IsNaN(y)) y = ymin;

            return y;
        }

        public void Reset()
        {
            calka = 0;   //zerowanie calki
        }
    }
    #endregion

    public enum eStanyPracyCentrali
    {
        Stop = 0,
        Praca = 1,
        RozruchWentylatora = 2,
        WychladzanieNagrzewnicy = 3,
        AlarmNagrzewnicy = 4,
        AlarmFrost = 5
    }

    public class cRegulator
    {
        // ******** tych zmiennych nie ruszamy - są wykorzystywane przez program wywołujący
        public cDaneWeWy DaneWejsciowe = null;   //wejście z ahuSim.exe
        public cDaneWeWy DaneWyjsciowe = null;   //wyjście przesyłane 
        public double Ts = 1;                    //czas, co jaki jest wywoływana procedura regulatora

        cRegulatorPI RegPI = new cRegulatorPI();
        cRegulatorPI RegPI2 = new cRegulatorPI();
        cRegulatorPI RegPI2_2etap = new cRegulatorPI();

        eStanyPracyCentrali StanPracyCentrali = eStanyPracyCentrali.Stop;

        double CzasOdStartu = 0;
        double CzasOdStopu = 0;
        double OpoznienieZalaczeniaNagrzewnicy_s = 10;
        double OpoznienieWylaczeniaWentylatora_s = 15;
        double TminNaw = 16;
        double TmaxNaw = 32;

        // Konstruktor klasy - ustawia bezpieczne, niezerowe parametry startowe regulatorów
        public cRegulator()
        {
            RegPI.kp = 2.0;
            RegPI.ki = 0.02;

            RegPI2.kp = 1.0;
            RegPI2.ki = 0.05;

            RegPI2_2etap.kp = 1.0;
            RegPI2_2etap.ki = 0.05;
        }

        // ***************************************************
        // funkcja wywoływana przez zewnętrzny program co czas Ts
        public int iWywolanie()
        {
            // odczyt danych wejściowych
            double t_zad = DaneWejsciowe.Czytaj(eZmienne.TempZadana_C);
            double t_pom = DaneWejsciowe.Czytaj(eZmienne.TempPomieszczenia_C);
            double t_naw = DaneWejsciowe.Czytaj(eZmienne.TempNawiewu_C);
            double t_czerp = DaneWejsciowe.Czytaj(eZmienne.TempCzerpni_C);
            double t_wyw = DaneWejsciowe.Czytaj(eZmienne.TempWywiewu_C);
            double t_za_odzyskiem = DaneWejsciowe.Czytaj(eZmienne.TempZaOdzyskiem_C);
            double t_wyrz = DaneWejsciowe.Czytaj(eZmienne.TempWyrzutni_C);

            bool boStart = DaneWejsciowe.Czytaj(eZmienne.PracaCentrali) > 0;

            // ograniczenia wartości min i max regulatorów
            RegPI.ymin = TminNaw;
            RegPI.ymax = TmaxNaw;

            RegPI2.ymin = 0;
            RegPI2.ymax = 100;

            RegPI2_2etap.ymin = 0;
            RegPI2_2etap.ymax = 100;

            // algorytm sterowania - baza bezpieczna (czyszczenie stanów na początku cyklu)
            double y_nagrz = 0;
            double y_bypass = 100.0; // Domyślnie bezpiecznie otwarty bypass
            double y_chl_procent = 0; // Wartość double (0.0 - 100.0) dla wyświetlacza

            bool boPracaWentylatoraNawiewu = false;
            bool boPracaWentylatoraWywiewu = false;
            bool boGrzanie1 = false;
            bool boGrzanie2 = false;
            bool boChlodnica = false;

            if (t_za_odzyskiem < 5)
            {
                StanPracyCentrali = eStanyPracyCentrali.AlarmFrost;
            }

            // stany pracy
            switch (StanPracyCentrali)
            {
                case eStanyPracyCentrali.Stop:
                    {
                        y_nagrz = 0;
                        y_bypass = 100.0;
                        boChlodnica = false;
                        boGrzanie1 = false;
                        boGrzanie2 = false;

                        boPracaWentylatoraNawiewu = false;
                        boPracaWentylatoraWywiewu = false;

                        RegPI.Reset();
                        RegPI2.Reset();
                        RegPI2_2etap.Reset();
                        CzasOdStartu = 0;

                        if (boStart)
                        {
                            StanPracyCentrali = eStanyPracyCentrali.RozruchWentylatora;
                        }
                        break;
                    }
                case eStanyPracyCentrali.RozruchWentylatora:
                    {
                        boPracaWentylatoraNawiewu = true;
                        boPracaWentylatoraWywiewu = true;
                        y_bypass = 100.0;
                        boChlodnica = false;

                        if (CzasOdStartu < OpoznienieZalaczeniaNagrzewnicy_s)
                        {
                            y_nagrz = 0;
                            boGrzanie1 = false;
                            boGrzanie2 = false;
                            CzasOdStartu += Ts;
                        }
                        else
                        {
                            StanPracyCentrali = eStanyPracyCentrali.Praca;
                        }
                        break;
                    }
                case eStanyPracyCentrali.Praca:
                    {
                        boPracaWentylatoraNawiewu = true;
                        boPracaWentylatoraWywiewu = true;

                        if (!boStart)
                        {
                            y_nagrz = 0;
                            boChlodnica = false;
                            y_bypass = 100.0;
                            boGrzanie1 = false;
                            boGrzanie2 = false;

                            CzasOdStopu = 0;
                            StanPracyCentrali = eStanyPracyCentrali.WychladzanieNagrzewnicy;
                        }
                        else
                        {
                            // 1. Regulator nadrzędny (Temperatura pomieszczenia)
                            double uchybPomZad = t_zad - t_pom;
                            double t_naw_zad = RegPI.Wyjscie(uchybPomZad);

                            // 2. Regulator podrzędny (Temperatura nawiewu)
                            double uchybNaw = t_naw_zad - t_naw;


                            // STREFA MARTWA 
                            if (uchybNaw > -2.0 && uchybNaw < 2.0)
                            {
                                y_bypass = 100.0;
                                y_nagrz = 0;
                                boChlodnica = false;
                                boGrzanie1 = false;
                                boGrzanie2 = false;

                                RegPI2_2etap.Reset();
                            }

                            // GRZANIE
                            else if (uchybNaw >= 2.0)
                            {
                                boChlodnica = false;

                                // bypass
                                double s_bypass = RegPI2.Wyjscie(uchybNaw);
                                y_bypass = 100.0 - s_bypass;

                                // Nagrzewnica
                                if (y_bypass <= 1.0)
                                {
                                    y_bypass = 0;
                                    y_nagrz = RegPI2_2etap.Wyjscie(uchybNaw);


                                    boGrzanie1 = false; 
                                    boGrzanie2 = true;  
                                }
                                else
                                {
                                    y_nagrz = 0;


                                    boGrzanie1 = true;  
                                    boGrzanie2 = false; 

                                    RegPI2_2etap.Reset();
                                }
                            }

                            // CHLODZENIE
                            else if (uchybNaw <= -2.0)
                            {
                                y_nagrz = 0;
                                boGrzanie1 = false;
                                boGrzanie2 = false;

                                double uchybModul = Math.Abs(uchybNaw);

                                // bypass
                                double s_bypass = RegPI2.Wyjscie(uchybModul);
                                y_bypass = 100.0 - s_bypass;

                                // Chlodnica
                                if (y_bypass <= 1.0)
                                {
                                    y_bypass = 0;

                                    boGrzanie1 = false;
                                    boGrzanie2 = false;

                                    double s_chlodnica = RegPI2_2etap.Wyjscie(uchybModul);
                                    boChlodnica = (s_chlodnica > 1.0);
                                }
                                else
                                {
                                    boChlodnica = false;
                                    RegPI2_2etap.Reset();
                                }
                            }

                            // Dodatkowa ochrona nagrzewnicy przed mrozem
                            if (t_czerp < 2.0 && uchybNaw > -2.0)
                            {
                                boGrzanie2 = true;
                            }
                        }
                        break;
                    }
                case eStanyPracyCentrali.WychladzanieNagrzewnicy:
                    {
                        if (CzasOdStopu < OpoznienieWylaczeniaWentylatora_s)
                        {
                            CzasOdStopu += Ts;
                            y_nagrz = 0;
                            boGrzanie1 = false;
                            boGrzanie2 = false;
                            boChlodnica = false;
                            boPracaWentylatoraNawiewu = true;
                            boPracaWentylatoraWywiewu = true;
                            y_bypass = 100.0;
                        }
                        else
                        {
                            StanPracyCentrali = eStanyPracyCentrali.Stop;
                        }
                        break;
                    }
                case eStanyPracyCentrali.AlarmNagrzewnicy:
                case eStanyPracyCentrali.AlarmFrost:
                    {
                        y_nagrz = 100;
                        boGrzanie1 = false;
                        boGrzanie2 = true;
                        boChlodnica = false;
                        y_bypass = 0;
                        boPracaWentylatoraNawiewu = false;
                        boPracaWentylatoraWywiewu = false;
                        break;
                    }
            }

            // 4. MAPOWANIE WYJŚĆ DO SYMULATORA (Bezpieczne typy danych)
            DaneWyjsciowe.Zapisz(eZmienne.WysterowanieNagrzewnicy1_pr, y_nagrz);
            DaneWyjsciowe.Zapisz(eZmienne.Wysterowanie_bypass_pr, y_bypass);

            // Konwersja stanu logicznego chłodnicy na double (0.0 lub 100.0) dla wyświetlacza
            y_chl_procent = boChlodnica ? 100.0 : 0.0;
            DaneWyjsciowe.Zapisz(eZmienne.WysterowanieChlodnicy_pr, y_chl_procent);

            // Zapis czystych typów Boolean do rejestrów binarnych pompy i wentylatorów
            DaneWyjsciowe.Zapisz(eZmienne.ZezwolenieNaPraceWentylatoraNawiewu, boPracaWentylatoraNawiewu);
            DaneWyjsciowe.Zapisz(eZmienne.ZezwolenieNaPraceWentylatoraWywiewu, boPracaWentylatoraWywiewu);
            DaneWyjsciowe.Zapisz(eZmienne.ZalaczeniePompyNagrzewnicyWodnej1, boGrzanie1);
            DaneWyjsciowe.Zapisz(eZmienne.ZalaczeniePompyNagrzewnicyWodnej2, boGrzanie2);
            DaneWyjsciowe.Zapisz(eZmienne.ZalaczeniePompyChlodnicyWodnej, boChlodnica);

            return 0;
        }

        // wywołanie formularza z parametrami
        public void ZmienParametry()
        {
            fmParametry fm = new fmParametry();
            fm.kp = RegPI.kp;
            fm.ki = RegPI.ki;

            fm.kp2 = RegPI2.kp;
            fm.ki2 = RegPI2.ki;

            fm.t1 = OpoznienieZalaczeniaNagrzewnicy_s;
            fm.t2 = OpoznienieWylaczeniaWentylatora_s;

            fm.TminNaw = TminNaw;
            fm.TmaxNaw = TmaxNaw;

            if (fm.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                RegPI.kp = fm.kp;
                RegPI.ki = fm.ki;

                RegPI2.kp = fm.kp2;
                RegPI2.ki = fm.ki2;

                RegPI2_2etap.kp = fm.kp2;
                RegPI2_2etap.ki = fm.ki2;

                TminNaw = fm.TminNaw;
                TmaxNaw = fm.TmaxNaw;

                OpoznienieZalaczeniaNagrzewnicy_s = fm.t1;
                OpoznienieWylaczeniaWentylatora_s = fm.t2;
            }
        }
    }
}