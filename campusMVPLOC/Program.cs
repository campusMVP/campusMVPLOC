/*
___ __ _ _ __ ___  _ __  _ _ ___ _ __ _____   ___ __   ___ ___
 / __/ _` | '_ ` _ \| '_ \| | | / __| '_ ` _ \ \ / | '_ \ / _ / __|
| (_| (_| | | | | | | |_) | |_| \__ | | | | | \ V /| |_) |  __\__ \
 \___\__,_|_| |_| |_| .__/ \__,_|___|_| |_| |_|\_/ | .__(_\___|___/
                    |_|                            |_|             
Utilidad sencilla de línea de comandos para realizar contaje de líneas de código y estadísticas de comentarios.
http://www.campusmvp.es
Creado por Josçé Manuel Alarcón (http://jasoft.org)
Licencia Apache 2.0.
*/
using System;
using System.Text.RegularExpressions;
using System.IO;
using System.Diagnostics;

namespace LOC
{
    class Program
    {
        //Auxiliares privadas
        private static string extensiones = "*.js,*.json,*.htm,*.html,*.css,*.ts,*.cs,*.vb,*.cpp,*.h,*.java";    //Las extensiones por defecto que se comprueban
        private static string tiposComentarioLineales = @"^[\s\t]*//.*?$,^[\s\t]*'.*?$";   //Comentarios identificados por defecto de una sola línea // y '. 
        //A mayores se detectarán los comentarios de tipo /* ... */ de manera separada para conseguir una sola pasada por cada archivo. 
        //En esta matriz se meten las aperturas y en la siguiente los correspondientes cierres
        private static Regex[] tiposComentariosBloqueAperturas = { new Regex(@".*/\*.*") }; // Incluidos: /*
        private static Regex[] tiposComentariosBloqueCierres = { new Regex(@".*\*/") }; //Tienen que corresponderse con los de apertura de la anterior. Incluidos: */
        //Se pueden extender con otros tipos de comentarios de bloque (multilínea) introduciendo los correspondientes bloques de apertura y cierre. Los anteriores son los más comunes.

        private static string exclusiones = "^.git$,^.svn$,^bin$,^obj$,^properties$"; //carpetas y archivos a excluir, por defecto los de control de código

        private static string[] tiposArchivo;   //Matriz con las extensiones que se van a buscar (normalmente generado de la lista de arriba)
        private static long numArchivos = 0;
        private static long numCarpetas = 0;
        private static long numLineas = 0;
        private static long numBlancas = 0;
        private static long numComentarios = 0;

        private static int nivelCarpeta = 0;
        private static bool modoSilencioso = false;

        private static Regex[] reComentarios = null;    //Expresiones regulares para detectar comentarios
        private static Regex reExclusiones = null;  //Expresión regular para detectar carpetas y archivos exluidos
        

        static void Main(string[] args)
        {
            bool batch = false;

            if (args.Length == 0 || (args[0] == "-?") || (args[0] == "/?"))
            {
                MuestraAyuda();
                return;
            }

            //Averiguamos la ruta de la carpeta a procesar
            string ruta = Path.GetFullPath(args[0]);

            //Comprobamos que existe
            if (!Directory.Exists(ruta))
            {
                Console.WriteLine("La carpeta \"{0}\" no existe.", ruta);
                return;
            }

            //Verificamos los parámetros, de haberlos
            foreach (string arg in args)
            {
                //Solo lo procesamos si es un modificador (empieza por - o / y tiene :)
                if (EsModificador(arg))
                {
                    //Valor del parámetro (de haberlo)
                    string valParam = "";
                    if (arg.Length>2)
                        valParam = arg.Substring(3); //Se toma desde la 4ª posición (después de los :) al final

                    //Vemos qué modificador es y actuamos en consecuencia
                    switch (arg.Substring(1, 1).ToLower())
                    {
                        case "f":
                            //Si se especifican unos tipos de archivo en particular, se sobrescriben los que hay por defecto
                            extensiones = valParam;
                            break;
                        case "c":
                            //Si se especifica la expresión regular para buscar comentarios, se añaden a las existentes
                            tiposComentarioLineales += ("," + valParam);
                            break;
                        case "x":
                            //Si se quieren excluir carpetas, se añaden a las exclusiones por defecto
                            //Se cambian las comas por "|"
                            exclusiones += ("|" + valParam.Replace(",", "|"));
                            break;
                        case "b":
                            batch = true;
                            break;
                        case "s":
                            modoSilencioso = true;
                            break;
                        case "?":
                            MuestraAyuda();
                            return;
                    }
                }
            }

            //Array con las extensiones de archivo que vamos a localizar
            tiposArchivo = extensiones.Split(",".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);

            //Inicializado expresiones regulares de comentarios
            string[] expComentarios = tiposComentarioLineales.Split(",".ToCharArray(),  StringSplitOptions.RemoveEmptyEntries);
            reComentarios = new Regex[expComentarios.Length];
            for (int i=0; i<expComentarios.Length; i++)
            {
                reComentarios[i] = new Regex(expComentarios[i], RegexOptions.IgnoreCase|RegexOptions.Multiline|RegexOptions.Singleline);
            }

            //Inicializo la Expresión regular para excluir archivos y carpetas
            exclusiones = exclusiones.Replace(",", "|");
            reExclusiones = new Regex(exclusiones, RegexOptions.IgnoreCase);

            log(String.Format("////// Comenzando a procesar la carpeta {0}", ruta));

            //Anoto la hora de comienzo
            var crono = Stopwatch.StartNew();

            //Se procesa recursivamente la carpeta indicada
            ProcesaCarpeta(ruta);

            //Detengo el cronómetro
            crono.Stop();

            //Se muestran los resultados en rojo
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("RESULTADOS: Num Carpetas: {0:N0}\nNum archivos: {1:N0}\nLíneas de Código ejecutables (LOC): {2:N0}\nLíneas en blanco (BLOC): {3:N0}\nLineas comentadas (CLOC): {4:N0}\nLíneas totales (): {5:N0}\nRatio Comentarios/Codigo: {6:F2}/1\nTiempo total empleado en el análisis: {7:N0}ms", numCarpetas, numArchivos, numLineas, numBlancas, numComentarios, numLineas + numBlancas + numComentarios, ((double)numComentarios / numLineas), crono.ElapsedMilliseconds);
            Console.ResetColor();

            log("////// Final del procesamiento");

            //Si no se indica lo contrario se detiene para asegurar que se ve el resultado por pantalla (y no se cierra la consola inmediatamente)
            //Si se le pone un parámetro "/b" o "-b" (de batch) entonces se termina el programa inmediatamente, para dejar que sigan ejecutándose otras instrucciones.
            if (!batch)
            {
                Console.ReadLine();
            }
        }

#region auxiliares
        private static void MuestraAyuda()
        {
            Console.WriteLine(@"Cuenta las líneas de código de los archivos de una carpeta. Debemos identificar los tipos de archivo a tener en cuenta y, 
opcionalmente, una expresión regular para buscar comentarios.");
            Console.WriteLine("\nUso: campusMVPLOC.exe carpeta_inicial -f:[tipos,archivo,separados,por,comas] -c:[RegExp para comentarios] -x:[RegExp para carpetas a excluir, separados por comas] -b -v");
            Console.WriteLine("\nEj: campusMVPLOC.exe c:\\MiPrograma");
            Console.WriteLine("\nEj: campusMVPLOC.exe c:\\MiPrograma -f:*.js,*.aspx");
            Console.WriteLine("\nEj: campusMVPLOC.exe c:\\MiPrograma -f:*.vbs, -c:'.* /x:^Privado_.*$ que buscaría comentarios de VBScript (empiezan por un apóstrofe) además de los comunes en archivo .vbs y excluyendo las carpetas que se llamen Privado_XXXX.\nSe puede usar / o . para los parámetros opcionales.");
            Console.WriteLine("\nPor defecto busca comentarios de tipo // y /* */ que son los habituales en casi todos los lenguajes, así como los comentarios con apóstrofe típicos de Visual Basic.");
            Console.WriteLine("\nSi no se especifican los tipos de archivo se usarán {0}.", extensiones);
            Console.WriteLine("\nSi se especifica el parámetro /b o -b se usará el modo 'batch' de forma que no se detendrá la ejecución para mostrar resultados por pantalla. Útil para guardar resultados a disco en una tarea desatendida.");
            Console.WriteLine("\nSi se especifica el parámetro /s o -s (ojo a los dos puntos al final) se usará el modo 'silencioso' y no se mostrará (como se hace por defecto) de todo lo que se va haciendo. Útil si no queremos auditar qué archivos y carpetas se han procesado.");
            Console.WriteLine("\npor José M. Alarcón, campusMVP [www.campusmvp.es]");
            Console.ReadLine();
        }

        /// <summary>
        /// Indica si el argumento de línea de comandos que se le pasa es un modificador o no, es decir
        /// si empieza o no por "/" o "-" y tiene dos puntos como tercera letra, por ejemplo -f: o /x:
        /// </summary>
        /// <param name="arg">El argumento de la línea de comandos</param>
        /// <returns></returns>
        private static bool EsModificador(string arg)
        {
            try {
                return ((arg.StartsWith("/") || arg.StartsWith("-")) && (arg.Length>2 ? arg.Substring(2, 1) == ":": true));
            }
            catch 
            {
                return false;
            }
        }

        /// <summary>
        /// Muestra por consola el mensaje indicado si no estamos en modo silencioso
        /// </summary>
        /// <param name="msg">El mensaje a mostrar</param>
        /// <param name="color">El color con el que mosrtar el mensaje.</param>
        private static void log(string msg, ConsoleColor color)
        {
            if (!modoSilencioso)
            {
                Console.ForegroundColor = color;
                Console.Write(msg + "\n");
                Console.ResetColor();
            }
        }

        /// <summary>
        /// Sobrecarga de la anterior que escribe en blanco
        /// </summary>
        /// <param name="msg"></param>
        private static void log(string msg)
        {
            log(msg, ConsoleColor.White);
        }

        /// <summary>
        /// Devuelve el separador del nivel actual en el árbol de carpetas, para sangrr apropiadamente la información
        /// </summary>
        /// <returns></returns>
        private static string separadorNivelActual()
        {
            return new String('-', nivelCarpeta);
        }

        /// <summary>
        /// Procesa la carpeta que se le indique y sus subcarpetas de forma recursiva en busca de archivos de código a procesar
        /// </summary>
        /// <param name="RutaCarpeta">La ruta en disco de la carpeta a procesar</param>
        private static void ProcesaCarpeta(string RutaCarpeta)
        {
            DirectoryInfo di = new DirectoryInfo(RutaCarpeta);
            ProcesaCarpeta(di);
        }

        /// <summary>
        /// Sobrecarga de la función que toma como parámetro un DirectoryInfo en lugar de una ruta física
        /// </summary>
        /// <param name="di">Información del directorio actual</param>
        private static void ProcesaCarpeta(DirectoryInfo di)
        {
            long numArchEnEstaCarpeta = 0;
            numCarpetas++;
            nivelCarpeta++; //Se aumenta el nivel de profundidad de la carpeta inspeccionada (para indicar visualmente dónde estamos)
            log(String.Format("{0} {1}", separadorNivelActual(), di.Name), ConsoleColor.Green);

            foreach (string t in tiposArchivo)
            {
                //Procesamos los archivos
                FileInfo[] archivos = di.GetFiles(t);
                foreach (FileInfo arch in archivos)
                {
                    numArchEnEstaCarpeta++;
                    CuentaLineasCodigo(arch);
                }
            }

            //Mostramos número de archivos procesados en la carpeta
            log(String.Format("{0} Total archivos de código en {1}: {2:N0}", separadorNivelActual(), di.Name, numArchEnEstaCarpeta), ConsoleColor.Green);

            //Ahora recorremos las subcarpetas pertinentes
            DirectoryInfo[] carpetas = di.GetDirectories();
            foreach (DirectoryInfo carpeta in carpetas)
            {
                //Compruebo que no sean carpetas excluídas
                if (reExclusiones.IsMatch(carpeta.Name))
                {
                    log(String.Format("{0} {1} (excluida)", separadorNivelActual(), carpeta.Name), ConsoleColor.Cyan);   //Van en rojo las que se hayan excluido
                }
                else
                {
                    ProcesaCarpeta(carpeta);
                }
            }

            nivelCarpeta--;
        }

        //Auxiliares para determinar si estamos dentro de un bloque comentado o no
        private static int tipoComentarioBloqueActual = -1;
        private static bool enBloqueComentado = false;

        /// <summary>
        /// Cuenta el número de líneas de código que hay en el archivo indicado, contando también vacías y comentarios.
        /// </summary>
        /// <param name="arch">Información sobre el archivo a leer</param>
        /// <returns></returns>
        private static int CuentaLineasCodigo(FileInfo arch)
        {
            numArchivos++;
            log(String.Format("{0}-> {1}", separadorNivelActual(), arch.Name), ConsoleColor.Yellow);

            using (StreamReader reader = File.OpenText(arch.FullName))
            {
                bool esVacia = false;
                bool esComentario = false;

                //Voy leyendo línea a línea y comprobamos si es una línea de código
                string linea = reader.ReadLine();
                while (linea != null)
                {
                    //Antes de nada comprueb si estoy en un bloque de comentario o no
                    if (enBloqueComentado)
                    {
                        esComentario = true;
                        //Ver si la línea actual cierra el comentario o no
                        if (tiposComentariosBloqueCierres[tipoComentarioBloqueActual].IsMatch(linea))
                        {
                            //Ya no estamos en un bloque a partir de ahora
                            enBloqueComentado = false;
                            tipoComentarioBloqueActual = -1;
                            /*
                            Con esta lógica, un caso en el que se contabilizarían mal líneas de código comentadas es cuando se cierra un comentario de bloque y se abre otro en la misma línea.
                            Lo considero un caso extremo que se dará muy poco (casi nunca) y asumo como un posible error de recuento mínimo en el total para no complicar el código.
                            */
                        }
                    }
                    else    //Línea de código normal, no dentro de un bloque comentado
                    {
                        //Veo si es una línea vacía (o solo con espacios/tabuladores)
                        esVacia = string.IsNullOrWhiteSpace(linea);

                        //Compruebo si es un comentario de los de una sola línea, los compruebo con las expresiones regulares
                        esComentario = false;
                        foreach (Regex re in reComentarios)
                        {
                            if (re.IsMatch(linea))
                                esComentario = true;
                        }

                        //Verifico comentarios en varias líneas
                        //Primero veo si hay una apertura de comentario
                        for (int i = 0; i < tiposComentariosBloqueAperturas.Length; i++)
                        {
                            Regex re = tiposComentariosBloqueAperturas[i];
                            if (re.IsMatch(linea))  //Si es un comentario de bloque
                            {
                                //Debo comprobar antes de nada que no se cierre en la misma línea
                                if (!tiposComentariosBloqueCierres[i].IsMatch(linea))
                                {
                                    //Si el bloque actual no se cierra en esta misma línea, 
                                    //marco a partir de ahora todas las nuevas líneas como un bloque comentado
                                    //hasta que aparezca el bloque de cierre
                                    tipoComentarioBloqueActual = i; //para localizar la etiqueta de cierre del bloque
                                    enBloqueComentado = true;
                                }
                                esComentario = true;    //La línea actual es un comentario
                                break;  //Salgo del bucle
                                /*
                                NOTA: este código, tal y como está, no identificaría líneas de código que estén justo después del cierre de un comentario de código multi-línea.
                                Entiendo que este es un caso muy extremo que se dará muy poco frecuentemente (porque no tiene tampoco mucho sentido), y lo asumo como un posible error mínimo de recuento de líneas de código.
                                */
                            }
                        }
                    }
                    if (esComentario)
                        numComentarios++;
                    else if (esVacia)
                        numBlancas++;
                    else
                        numLineas++;
                    //Siguiente línea
                    linea = reader.ReadLine();
                }
            }

            return 0;
        }
        #endregion
    }
}
