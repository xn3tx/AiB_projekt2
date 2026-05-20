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

        // ograniczenia wysterowania (przypisanie wartości w iWywolanie bo różne dla PI1 i PI2)
        public double ymin;
        public double ymax;

        public double Wyjscie(double Uchyb)
        {
            double y = kp * Uchyb + ki * calka / 60;  //wyjście regulatora

            //anti wind-up
            if (y < ymax && y > ymin)
            {
                calka = calka + Uchyb * Ts;
            }
            y = kp * Uchyb + ki * calka / 60;

            //nasycenie
            if (y > ymax)
            {
                y = ymax;
            }

            if (y < ymin)
            {
                y = ymin;
            }
            return y;
        }


        public void Reset()
        {
            calka = 0;   //zerowanie calki
        }
    }
    #endregion



    // przykładowe stany pracy centrali - do zmiany pod kątem właściwego projektu
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

        //PI - zewnętrzny, sterujący temperaturą nawiewu
        //PI2 - wewnętrzny, sterujący % otwarcia zaworu
        //sterowanie zaworem chłodnicy też przez PI2???
        cRegulatorPI RegPI = new cRegulatorPI();
        cRegulatorPI RegPI2 = new cRegulatorPI();

        eStanyPracyCentrali StanPracyCentrali = eStanyPracyCentrali.Stop;

        double CzasOdStartu = 0;
        double CzasOdStopu = 0;
        double OpoznienieZalaczeniaNagrzewnicy_s = 10;
        double OpoznienieWylaczeniaWentylatora_s = 15;
        double TminNaw = 16;
        double TmaxNaw = 32;




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

            // ograniczenia wartości min i max regulatora PI1
            RegPI.ymin = TminNaw;
            RegPI.ymax = TmaxNaw;

            // ograniczenia wartości min i max regulatora PI2
            RegPI2.ymin = 0;
            RegPI2.ymax = 100;


            // algorytm sterowania
            double y_nagrz = 0;
            double y_chl = 0;
            double y_bypass = 0;
            bool boPracaWentylatoraNawiewu = false;
            bool boPracaWentylatoraWywiewu = false;
            bool boPompaNagrzewnicy = false;

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
                        boPracaWentylatoraNawiewu = false;
                        RegPI.Reset();
                        RegPI2.Reset();
                        if (boStart)
                        {
                            StanPracyCentrali = eStanyPracyCentrali.RozruchWentylatora;
                        }
                        break;
                    }
                case eStanyPracyCentrali.RozruchWentylatora:
                    {
                        boPracaWentylatoraNawiewu = true;

                        if (CzasOdStartu < OpoznienieZalaczeniaNagrzewnicy_s)
                        {
                            y_nagrz = 0;
                            boPompaNagrzewnicy = Convert.ToBoolean(y_nagrz);
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

                        if (!boStart)
                        {
                            y_nagrz = 0;
                            y_chl = 0;
                            boPompaNagrzewnicy = Convert.ToBoolean(y_nagrz);
                            StanPracyCentrali = eStanyPracyCentrali.WychladzanieNagrzewnicy;
                        }
                        else
                        {
                            //najpierw bajpas, drugi priorytet na grzanie/chłodzenie
                            //dodać chłodzenie i jakąś strefe martwą grzanie-chłodzenie

                            // regulator PI temperatura nawiewu
                            double uchybPomZad = t_zad - t_pom;
                            double t_naw_zad = RegPI.Wyjscie(uchybPomZad);

                            // regulator PI2 wysterowanie nagrzewnicy
                            double uchybNaw = t_naw_zad - t_naw;
                            y_nagrz = RegPI2.Wyjscie(uchybNaw);
                            boPompaNagrzewnicy = Convert.ToBoolean(y_nagrz);
                        }
                        
                        break;
                    }
                case eStanyPracyCentrali.WychladzanieNagrzewnicy:
                    {
                        if (CzasOdStopu < OpoznienieWylaczeniaWentylatora_s)
                        {
                            CzasOdStopu += Ts;
                            y_nagrz = 0;
                            boPompaNagrzewnicy = Convert.ToBoolean(y_nagrz);
                            boPracaWentylatoraNawiewu = true;
                        }
                        else
                        {
                            StanPracyCentrali = eStanyPracyCentrali.Stop;
                            y_nagrz = 0;
                            boPompaNagrzewnicy = Convert.ToBoolean(y_nagrz);
                            boPracaWentylatoraNawiewu = false;
                        }

                        break;
                    }
                case eStanyPracyCentrali.AlarmNagrzewnicy:
                    {
                        y_nagrz = 100;
                        boPompaNagrzewnicy = Convert.ToBoolean(y_nagrz);
                        //zamknięcie przepustnic
                        //po 5 minutach włączenie centrali
                        break;
                    }
                case eStanyPracyCentrali.AlarmFrost:
                    {
                        break;
                    }
            }


            // ustawienie wyjść
            DaneWyjsciowe.Zapisz(eZmienne.WysterowanieNagrzewnicy1_pr, y_nagrz);
            DaneWyjsciowe.Zapisz(eZmienne.ZezwolenieNaPraceWentylatoraNawiewu, boPracaWentylatoraNawiewu);
            DaneWyjsciowe.Zapisz(eZmienne.ZezwolenieNaPraceWentylatoraWywiewu, boPracaWentylatoraWywiewu);
            DaneWyjsciowe.Zapisz(eZmienne.ZalaczeniePompyNagrzewnicyWodnej1, boPompaNagrzewnicy);
            DaneWyjsciowe.Zapisz(eZmienne.WysterowanieChlodnicy_pr, y_chl);
            DaneWyjsciowe.Zapisz(eZmienne.Wysterowanie_bypass_pr, y_bypass);
            return 0;
        }
       






        // wywołanie formularza z parametrami
        public void ZmienParametry()
        {
            // wnętrze funkcji dowolnie zmieniane przez studenta
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

                TminNaw = fm.TminNaw;
                TmaxNaw = fm.TmaxNaw;

                OpoznienieZalaczeniaNagrzewnicy_s = fm.t1;
                OpoznienieWylaczeniaWentylatora_s = fm.t2;

            }
        }
    }
}
