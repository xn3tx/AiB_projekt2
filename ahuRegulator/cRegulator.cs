using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ahuKlasy;

namespace ahuRegulator
{

    #region ParametryLokalne

    // przykładowa realizacja - do modyfikacji przez studenta
    class cRegulatorPI
    {
        double Ts = 1;  
        public double calka = 0;

        public double kp = 1;
        public double ki = 0;


        public double Wyjscie(double Uchyb)
        {
            calka = calka + Uchyb * Ts;
            return kp * Uchyb + ki * calka/60;
        }
    }
    #endregion



    // przykładowe stany pracy centrali - do zmiany podkądem właściwego projektu
    public enum eStanyPracyCentrali
    {
        Stop = 0,
        Praca = 1,
        RozruchWentylatora = 2,
        WychladzanieNagrzewnicy = 3,
        AlarmNagrzewnicy = 4
    }



    public class cRegulator
    {
        // ******** tych zmiennych nie ruszamy - są wykorzystywane przez program wywołujący
        public cDaneWeWy DaneWejsciowe = null;   //wejście z ahuSim.exe
        public cDaneWeWy DaneWyjsciowe = null;   //wyjście przesyłane 
        public double Ts = 1;                    //czas, co jaki jest wywoływana procedura regulatora


        // ********* zmienne definiowane przez studenta
        cRegulatorPI RegPI = new cRegulatorPI();


        eStanyPracyCentrali StanPracyCentrali = eStanyPracyCentrali.Stop;

        double CzasOdStartu = 0;
        double CzasOdStopu = 0;
        double OpoznienieZalaczeniaNagrzewnicy_s = 10;
        double OpoznienieWylaczeniaWentylatora_s = 15;



        // ***************************************************
        // funkcja wywoływana przez zewnętrzny program co czas Ts
        public int iWywolanie()    
        {
            // wnętrze funkcji dowolnie zmieniane przez studenta


            // przykład odczytu danych wejściowych
            double t_zad = DaneWejsciowe.Czytaj(eZmienne.TempZadana_C);
            double t_pom = DaneWejsciowe.Czytaj(eZmienne.TempPomieszczenia_C);
            double t_naw = DaneWejsciowe.Czytaj(eZmienne.TempNawiewu_C);
            double t_czerp = DaneWejsciowe.Czytaj(eZmienne.TempCzerpni_C);
            double t_wyw = DaneWejsciowe.Czytaj(eZmienne.TempWywiewu_C);

            bool boStart = DaneWejsciowe.Czytaj(eZmienne.PracaCentrali) > 0;





            // algorytm sterowania
            double y_nagrz = 0;
            
            bool boPracaWentylatoraNawiewu = false;



            switch (StanPracyCentrali)
            {
                case eStanyPracyCentrali.Stop:
                    {
                        y_nagrz = 0;
                        boPracaWentylatoraNawiewu = false;
                        if(boStart)
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
                            CzasOdStartu += Ts;
                        }
                        else
                        {
                            StanPracyCentrali = eStanyPracyCentrali.Praca;
                            y_nagrz = RegPI.Wyjscie(t_zad - t_pom);
                        }


                        break;
                    }
                case eStanyPracyCentrali.Praca:
                    {
                        boPracaWentylatoraNawiewu = true;

                        if (!boStart)
                        {
                            y_nagrz = 0;
                            StanPracyCentrali = eStanyPracyCentrali.WychladzanieNagrzewnicy;
                        }
                        else
                        {
                            y_nagrz = RegPI.Wyjscie(t_zad - t_pom);
                        }
                        
                        break;
                    }
                case eStanyPracyCentrali.WychladzanieNagrzewnicy:
                    {
                        if (CzasOdStopu < OpoznienieWylaczeniaWentylatora_s)
                        {
                            CzasOdStopu += Ts;
                            y_nagrz = 0;
                            boPracaWentylatoraNawiewu = true;
                        }
                        else
                        {
                            StanPracyCentrali = eStanyPracyCentrali.Stop;
                            y_nagrz = 0;
                            boPracaWentylatoraNawiewu = true;
                        }

                        break;
                    }
            }


            // ustawienie wyjść
            DaneWyjsciowe.Zapisz(eZmienne.WysterowanieNagrzewnicy1_pr, y_nagrz);
            DaneWyjsciowe.Zapisz(eZmienne.ZezwolenieNaPraceWentylatoraNawiewu, boPracaWentylatoraNawiewu);
            return 0;
        }







        // wywołanie formularza z parametrami
        public void ZmienParametry()
        {
            // wnętrze funkcji dowolnie zmieniane przez studenta
            fmParametry fm = new fmParametry();
            fm.kp = RegPI.kp;
            fm.ki = RegPI.ki;

            fm.t1 = OpoznienieZalaczeniaNagrzewnicy_s;
            fm.t2 = OpoznienieWylaczeniaWentylatora_s;


            if (fm.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                RegPI.kp = fm.kp;
                RegPI.ki = fm.ki;

                OpoznienieZalaczeniaNagrzewnicy_s = fm.t1;
                OpoznienieWylaczeniaWentylatora_s = fm.t2;

            }
        }

    }
}
