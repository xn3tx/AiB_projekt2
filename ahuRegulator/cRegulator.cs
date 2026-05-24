using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
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
        AlarmFrost = 4,
        AlarmPPoz = 5
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
            // odczyt danych wejściowych - wejścia analogowe
            double t_zad = DaneWejsciowe.Czytaj(eZmienne.TempZadana_C);
            double t_pom = DaneWejsciowe.Czytaj(eZmienne.TempPomieszczenia_C);
            double t_naw = DaneWejsciowe.Czytaj(eZmienne.TempNawiewu_C);
            double t_czerp = DaneWejsciowe.Czytaj(eZmienne.TempCzerpni_C);
            double t_wyw = DaneWejsciowe.Czytaj(eZmienne.TempWywiewu_C);
            double t_za_odzyskiem = DaneWejsciowe.Czytaj(eZmienne.TempZaOdzyskiem_C);
            double t_wyrz = DaneWejsciowe.Czytaj(eZmienne.TempWyrzutni_C);

            // wejścia cyfrowe
            bool boStart = DaneWejsciowe.Czytaj(eZmienne.PracaCentrali) > 0;
            bool frost = DaneWejsciowe.Czytaj(eZmienne.TermostatPZamrNagrzewnicyWodnej) > 0;
            bool presostatNawWyw = DaneWejsciowe.Czytaj(eZmienne.PresostatWentylatoraNawiewu) > 0;
            bool weAlarmPPoz = DaneWejsciowe.Czytaj(eZmienne.WeAlarmPPoz) > 0;


            // wyjścia analogowe
            double y_nagrz = 0;
            double y_bypass = 100.0; 
            double y_chl_procent = 0; 
            double t_naw_zad = 0;

            // wyjścia cyfrowe
            bool boPracaWentylatoraNawiewu = false;
            bool boPracaWentylatoraWywiewu = false;
            bool boBypass = false;
            bool boGrzanie2 = false;
            bool boChlodnica = false;
            bool boPrzepustnice = false;
            bool boAlarmPresostat = false;
            bool boAlarmPPoz = false;
            bool boZmniejszObrNaw = false;
            bool boRozruch = false;
            bool boWychladzanie = false;


            double s_chlodnica;
            double s_bypass;
            double uchybPomZad;
            double uchybNaw;
            bool strefaMartwa = false;

            // ograniczenia wartości min i max regulatorów
            RegPI.ymin = TminNaw;
            RegPI.ymax = TmaxNaw;

            RegPI2.ymin = 0;
            RegPI2.ymax = 100;

            RegPI2_2etap.ymin = 0;
            RegPI2_2etap.ymax = 100;


            // stany pracy
            switch (StanPracyCentrali)
            {
                case eStanyPracyCentrali.Stop:
                    {
                        y_nagrz = 0; y_bypass = 100.0; y_chl_procent = 0; t_naw_zad = 0;

                        boPracaWentylatoraNawiewu = false; boPracaWentylatoraWywiewu = false;
                        boBypass = false; boGrzanie2 = false; boChlodnica = false; boPrzepustnice = false;
                        boAlarmPresostat = false; boAlarmPPoz = false; boZmniejszObrNaw = false;

                        RegPI.Reset();
                        RegPI2.Reset();
                        RegPI2_2etap.Reset();
                        CzasOdStartu = 0;

                        if (boStart && weAlarmPPoz==false)
                        {
                            StanPracyCentrali = eStanyPracyCentrali.RozruchWentylatora;
                        }
                        else if (weAlarmPPoz==true)
                        {
                            StanPracyCentrali = eStanyPracyCentrali.AlarmPPoz;
                        }
                        break;
                    }

                case eStanyPracyCentrali.RozruchWentylatora:
                    {
                        if (!boStart)
                        {
                            y_nagrz = 0; y_chl_procent = 0; y_bypass = 100.0; t_naw_zad = 0;
                            boChlodnica = false; boBypass = false; boGrzanie2 = false;
                            boPrzepustnice = true; boPracaWentylatoraNawiewu = true; boPracaWentylatoraWywiewu = true;
                            boAlarmPresostat = false; boAlarmPPoz = false; boZmniejszObrNaw = false;

                            CzasOdStopu = 0;
                            StanPracyCentrali = eStanyPracyCentrali.WychladzanieNagrzewnicy;
                        }
                        else
                        {
                            // 1. Alarm PPoż
                            if (weAlarmPPoz == true)
                            {
                                y_nagrz = 0; y_bypass = 100.0; t_naw_zad = 0;
                                boBypass = false; boGrzanie2 = false; boChlodnica = false;
                                boPracaWentylatoraNawiewu = false; boPracaWentylatoraWywiewu = false; boPrzepustnice = false;
                                boAlarmPresostat = false; boAlarmPPoz = true; boZmniejszObrNaw = false;
                                StanPracyCentrali = eStanyPracyCentrali.AlarmPPoz;
                            }

                            // 2. Zabezpieczenie frost
                            // 3. Zabezpieczenie układu odzysku stopień 2 (dla krytycznie niskich temperatur)
                            else if (frost == true || t_za_odzyskiem < 2)
                            {
                                y_nagrz = 100; y_bypass = 0; t_naw_zad = 0;
                                boBypass = false; boGrzanie2 = true; boChlodnica = false;
                                boPracaWentylatoraNawiewu = false; boPracaWentylatoraWywiewu = false; boPrzepustnice = false;
                                boAlarmPresostat = false; boAlarmPPoz = false; boZmniejszObrNaw = false;
                                StanPracyCentrali = eStanyPracyCentrali.AlarmFrost;
                            }

                            // 4. Normalna praca
                            else
                            {
                                boRozruch = true;
                                y_nagrz = 0; y_bypass = 100.0; y_chl_procent = 0; t_naw_zad = 0;
                                boPracaWentylatoraNawiewu = true; boPracaWentylatoraWywiewu = true;
                                boBypass = false; boGrzanie2 = false; boChlodnica = false;
                                boPrzepustnice = true; boAlarmPPoz = false; boZmniejszObrNaw = false;

                                // 5. alarm presostat nawiew/wywiew (jeden alarm ze względu na taką samą logikę działania)
                                if (presostatNawWyw == true)
                                {
                                    boAlarmPresostat = true;
                                }
                                else boAlarmPresostat = false;

                                // 6. zabezpieczenie układu odzysku stopień 1
                                if (t_za_odzyskiem < 5)
                                {
                                    boZmniejszObrNaw = true;
                                }

                                if (CzasOdStartu < OpoznienieZalaczeniaNagrzewnicy_s)
                                {
                                    CzasOdStartu += Ts;
                                }
                                else
                                {
                                    StanPracyCentrali = eStanyPracyCentrali.Praca;
                                }
                            }
                        }
                        break;
                    }

                case eStanyPracyCentrali.Praca:
                    {
                        boPracaWentylatoraNawiewu = true; boPracaWentylatoraWywiewu = true; boPrzepustnice = true;
                        boAlarmPPoz = false; boZmniejszObrNaw = false;

                        if (!boStart)
                        {
                            y_nagrz = 0; y_chl_procent = 0; y_bypass = 100.0; t_naw_zad = 0;
                            boChlodnica = false; boBypass = false; boGrzanie2 = false;
                            boPrzepustnice = true; boAlarmPresostat = false; boAlarmPPoz = false; boZmniejszObrNaw = false;

                            CzasOdStopu = 0;
                            StanPracyCentrali = eStanyPracyCentrali.WychladzanieNagrzewnicy;
                        }
                        else
                        {
                            // Regulator nadrzędny (Temperatura pomieszczenia)
                            uchybPomZad = t_zad - t_pom;
                            t_naw_zad = RegPI.Wyjscie(uchybPomZad);

                            // Regulator podrzędny (Temperatura nawiewu)
                            uchybNaw = t_naw_zad - t_naw;


                            // 1. Alarm PPoż
                            if (weAlarmPPoz == true)
                            {
                                y_nagrz = 0; y_bypass = 100.0; t_naw_zad = 0;
                                boBypass = false; boGrzanie2 = false; boChlodnica = false;
                                boPracaWentylatoraNawiewu = false; boPracaWentylatoraWywiewu = false; boPrzepustnice = false;
                                boAlarmPresostat = false; boAlarmPPoz = true; boZmniejszObrNaw = false;
                                StanPracyCentrali = eStanyPracyCentrali.AlarmPPoz;
                            }

                            // 2. Zabezpieczenie frost
                            // 3. Zabezpieczenie układu odzysku stopień 2 (dla krytycznie niskich temperatur)
                            else if (frost == true || t_za_odzyskiem < 2)
                            {
                                y_nagrz = 100; y_bypass = 0; t_naw_zad = 0;
                                boBypass = false; boGrzanie2 = true; boChlodnica = false;
                                boPracaWentylatoraNawiewu = false; boPracaWentylatoraWywiewu = false; boPrzepustnice = false;
                                boAlarmPresostat = false; boAlarmPPoz = false; boZmniejszObrNaw = false;
                                StanPracyCentrali = eStanyPracyCentrali.AlarmFrost;
                            }

                            // 4. Normalna praca
                            else
                            {
                                // flaga strefy martwej (bo wyjście regulatora niestabilne) NIE DZIAŁA
                                if (Math.Abs(uchybNaw) < 2)
                                    strefaMartwa = true;

                                if (Math.Abs(uchybNaw) >= 2)
                                    strefaMartwa = false;

                                // 5. alarm presostat nawiew/wywiew (jeden alarm ze względu na taką samą logikę działania)
                                if (presostatNawWyw == true)
                                {
                                    boAlarmPresostat = true;
                                }
                                else boAlarmPresostat = false;

                                // 6. zabezpieczenie układu odzysku stopień 1
                                if (t_za_odzyskiem < 5)
                                {
                                    boZmniejszObrNaw = true;
                                }

                                // STREFA MARTWA 
                                if (strefaMartwa)
                                {
                                    t_naw_zad = RegPI.Wyjscie(uchybPomZad);
                                    y_bypass = 100.0; y_nagrz = 0; y_chl_procent = 0;
                                    boChlodnica = false; boBypass = false; boGrzanie2 = false;
                                    s_chlodnica = 0;

                                    RegPI2.Reset();
                                    RegPI2_2etap.Reset();
                                }

                                // GRZANIE
                                else if (uchybNaw >= 2.0)
                                {
                                    boChlodnica = false;
                                    y_chl_procent = 0;

                                    // bypass
                                    s_bypass = RegPI2.Wyjscie(uchybNaw);
                                    y_bypass = 100.0 - s_bypass;
                                  
                                    // Nagrzewnica
                                    if (y_bypass <= 1.0 || t_za_odzyskiem<5)
                                    {
                                        if (t_za_odzyskiem < 5) y_bypass = 100.0;
                                        else y_bypass = 0.0;
                                        y_nagrz = RegPI2_2etap.Wyjscie(uchybNaw);

                                        boBypass = false;
                                        boGrzanie2 = true;
                                    }
                                    else
                                    {
                                        y_nagrz = 0;

                                        boBypass = true;
                                        boGrzanie2 = false;

                                        RegPI2_2etap.Reset();
                                        }
                                    }

                                // CHLODZENIE
                                else if (uchybNaw <= -2.0)
                                {
                                    y_nagrz = 0;
                                    boBypass = false;
                                    boGrzanie2 = false;

                                    double uchybModul = Math.Abs(uchybNaw);

                                    // bypass
                                    s_bypass = RegPI2.Wyjscie(uchybModul);
                                    y_bypass = 100.0 - s_bypass;

                                    // Chlodnica
                                    if (y_bypass <= 1.0 || t_za_odzyskiem<5)
                                    {
                                        if (t_za_odzyskiem < 5)
                                        {
                                            y_bypass = 100.0;
                                            boChlodnica = false;
                                        }
                                        else
                                        {
                                            y_bypass = 0.0;
                                            boBypass = false;
                                            s_chlodnica = RegPI2_2etap.Wyjscie(uchybModul);
                                            boChlodnica = (s_chlodnica > 1.0);
                                        }
                                    }
                                    else
                                    {
                                        boBypass = true;
                                        boChlodnica = false;
                                        RegPI2_2etap.Reset();
                                    }
                                }
                            }
                        }
                        break;
                    }

                case eStanyPracyCentrali.WychladzanieNagrzewnicy:
                    {
                        if (CzasOdStopu < OpoznienieWylaczeniaWentylatora_s)
                        {
                            CzasOdStopu += Ts;
                            boWychladzanie = true;
                            y_nagrz = 0; y_bypass = 100.0; y_chl_procent = 0; t_naw_zad = 0;
                            boBypass = false; boGrzanie2 = false; boChlodnica = false;
                            boPracaWentylatoraNawiewu = true; boPracaWentylatoraWywiewu = true; boPrzepustnice = true;
                            boAlarmPresostat = false; boAlarmPPoz = false; boZmniejszObrNaw = false;
                        }
                        else
                        {
                            StanPracyCentrali = eStanyPracyCentrali.Stop;
                        }
                        break;
                    }

                case eStanyPracyCentrali.AlarmFrost:
                    {
                        if (!boStart)
                        {
                            y_nagrz = 0; y_bypass = 100.0; y_chl_procent = 0; t_naw_zad = 0;
                            boChlodnica = false; boBypass = false; boGrzanie2 = false;
                            boPracaWentylatoraNawiewu = false; boPracaWentylatoraWywiewu = false; boPrzepustnice = false;
                            boAlarmPresostat = false; boAlarmPPoz = false; boZmniejszObrNaw = false;

                            CzasOdStopu = 0;
                            StanPracyCentrali = eStanyPracyCentrali.Stop;
                        }
                        else
                        {
                            if (weAlarmPPoz == true)
                            {
                                y_nagrz = 100; y_bypass = 0; t_naw_zad = 0;
                                boBypass = false; boGrzanie2 = true; boChlodnica = false;
                                boPracaWentylatoraNawiewu = false; boPracaWentylatoraWywiewu = false; boPrzepustnice = false;
                                boAlarmPresostat = false; boAlarmPPoz = false; boZmniejszObrNaw = false;
                                StanPracyCentrali = eStanyPracyCentrali.AlarmPPoz;
                            }
                            else if (frost == true || t_za_odzyskiem<2)
                            {
                                y_nagrz = 100; y_bypass = 0; t_naw_zad = 0;
                                boBypass = false; boGrzanie2 = true; boChlodnica = false;
                                boPracaWentylatoraNawiewu = false; boPracaWentylatoraWywiewu = false; boPrzepustnice = false;
                                boAlarmPresostat = false; boAlarmPPoz = false; boZmniejszObrNaw = false;
                            }
                            else
                            {
                                CzasOdStartu = 0;
                                StanPracyCentrali = eStanyPracyCentrali.RozruchWentylatora;
                            }
                        }
                        break;
                    }

                case eStanyPracyCentrali.AlarmPPoz:
                    {
                        if (!boStart)
                        {
                            y_nagrz = 0; y_bypass = 100.0; y_chl_procent = 0; t_naw_zad = 0;
                            boChlodnica = false; boBypass = false; boGrzanie2 = false;
                            boPracaWentylatoraNawiewu = false; boPracaWentylatoraWywiewu = false; boPrzepustnice = false;
                            boAlarmPresostat = false; boAlarmPPoz = false; boZmniejszObrNaw = false;

                            CzasOdStopu = 0;
                            StanPracyCentrali = eStanyPracyCentrali.Stop;
                        }
                        else
                        {
                            if (weAlarmPPoz == true)
                            {
                                y_nagrz = 0; y_bypass = 100.0; t_naw_zad = 0;
                                boBypass = false; boGrzanie2 = false; boChlodnica = false;
                                boPracaWentylatoraNawiewu = false; boPracaWentylatoraWywiewu = false; boPrzepustnice = false;
                                boAlarmPresostat = false; boAlarmPPoz = true; boZmniejszObrNaw = false;
                            }
                            else
                            {
                                CzasOdStartu = 0;
                                StanPracyCentrali = eStanyPracyCentrali.RozruchWentylatora;
                            }
                        }
                            break;
                    }
            }

            // wyjścia analogowe
            DaneWyjsciowe.Zapisz(eZmienne.WysterowanieNagrzewnicy1_pr, y_nagrz);
            DaneWyjsciowe.Zapisz(eZmienne.Wysterowanie_bypass_pr, y_bypass);
            // Konwersja stanu logicznego chłodnicy na double (0.0 lub 100.0) dla wyświetlacza
            y_chl_procent = boChlodnica ? 100.0 : 0.0;
            DaneWyjsciowe.Zapisz(eZmienne.WysterowanieChlodnicy_pr, y_chl_procent);
            DaneWyjsciowe.Zapisz(eZmienne.WyjsciePI1, t_naw_zad);

            // wyjścia cyfrowe
            DaneWyjsciowe.Zapisz(eZmienne.ZezwolenieNaPraceWentylatoraNawiewu, boPracaWentylatoraNawiewu);
            DaneWyjsciowe.Zapisz(eZmienne.ZezwolenieNaPraceWentylatoraWywiewu, boPracaWentylatoraWywiewu);
            DaneWyjsciowe.Zapisz(eZmienne.ZalaczeniePompyNagrzewnicyWodnej1, boBypass);
            DaneWyjsciowe.Zapisz(eZmienne.ZalaczeniePompyNagrzewnicyWodnej2, boGrzanie2);
            DaneWyjsciowe.Zapisz(eZmienne.ZalaczeniePompyChlodnicyWodnej, boChlodnica);
            DaneWyjsciowe.Zapisz(eZmienne.PrzepustniceNawWyw, boPrzepustnice);
            DaneWyjsciowe.Zapisz(eZmienne.AlarmPresostat, boAlarmPresostat);
            DaneWyjsciowe.Zapisz(eZmienne.AlarmPPoz, boAlarmPPoz);
            DaneWyjsciowe.Zapisz(eZmienne.ZmniejszObrotyNaw, boZmniejszObrNaw);
            DaneWyjsciowe.Zapisz(eZmienne.Rozruch, boRozruch);
            DaneWyjsciowe.Zapisz(eZmienne.Wychladzanie, boWychladzanie);

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