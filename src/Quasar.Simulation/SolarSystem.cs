namespace Quasar.Simulation;

internal static class SolarSystem
{
    internal const string Sun = "star-sun";
    internal const string Earth = "planet-earth";
    internal const string Epoch = "2026-11-09 00:00 TDB";

    // JPL Horizons osculating elements, JD 2461353.5 TDB, J2000 ecliptic, km and degrees.
    // Target and center IDs follow each entry. Physical parameters are rounded reference values.
    internal static CelestialPositionService CreateCatalogue() => new(
    [
        new(Sun, "Sun", CelestialBodyKind.Star, 1.32712440018e11, RadiusKm: 695700),
        Body("planet-mercury", "Mercury", CelestialBodyKind.Planet, 22031.86855, 2439.7, "star-sun",
            5.790902525215314E+07, 2.056379962569973E-01, 7.003371555271872E+00, 4.829721104113197E+01, 2.920068706670232E+01, 3.544604542073831E+02), // Horizons 199 @ 10
        Body("planet-venus", "Venus", CelestialBodyKind.Planet, 324858.592, 6051.8, "star-sun",
            1.082100034637509E+08, 6.745702910360504E-03, 3.394441002224495E+00, 7.660311927942416E+01, 5.496628550530810E+01, 2.849044718868350E+02), // Horizons 299 @ 10
        Body("planet-earth", "Earth", CelestialBodyKind.Planet, 398600.435436, 6378.137, "star-sun",
            1.497282733911069E+08, 1.712334069341918E-02, 3.158383402171255E-03, 1.696901395169623E+02, 2.910006461241026E+02, 3.071170676246918E+02), // Horizons 399 @ 10
        Body("planet-mars", "Mars", CelestialBodyKind.Planet, 42828.3758, 3396.2, "star-sun",
            2.279476056874402E+08, 9.339804849542402E-02, 1.847435024492066E+00, 4.948027961550552E+01, 2.866654327520563E+02, 1.192869887292644E+02), // Horizons 499 @ 10
        Body("planet-jupiter", "Jupiter", CelestialBodyKind.Planet, 126686531.9, 71492, "star-sun",
            7.782466041318030E+08, 4.833729912629208E-02, 1.303751521086685E+00, 1.005224706284840E+02, 2.735179426170280E+02, 1.153240013403963E+02), // Horizons 599 @ 10
        Body("planet-saturn", "Saturn", CelestialBodyKind.Planet, 37931206.2, 60268, "star-sun",
            1.427058429406145E+09, 5.516090217455521E-02, 2.488591036935841E+00, 1.137089265855931E+02, 3.387503443112854E+02, 2.858731597943753E+02), // Horizons 699 @ 10
        Body("planet-uranus", "Uranus", CelestialBodyKind.Planet, 5793951.3, 25559, "star-sun",
            2.881537179112774E+09, 4.761023947142705E-02, 7.754603433749728E-01, 7.397762109505504E+01, 9.261923477775090E+01, 2.618492486211563E+02), // Horizons 799 @ 10
        Body("planet-neptune", "Neptune", CelestialBodyKind.Planet, 6835099.5, 24764, "star-sun",
            4.496316823571342E+09, 9.681923889345924E-03, 1.771767520980149E+00, 1.318479637568670E+02, 2.835537897931607E+02, 3.083724918611871E+02), // Horizons 899 @ 10
        Body("dwarfplanet-pluto", "Pluto", CelestialBodyKind.DwarfPlanet, 869.6, 1188.3, "star-sun",
            5.871209426050296E+09, 2.453957836572503E-01, 1.694522782216557E+01, 1.101289500599036E+02, 1.130629904574021E+02, 5.495518082405135E+01), // Horizons 999 @ 10
        Body("dwarfplanet-ceres", "Ceres", CelestialBodyKind.DwarfPlanet, 62.63, 469.7, "star-sun",
            4.138311673202831E+08, 7.982044635706349E-02, 1.058687958113027E+01, 8.024856805425620E+01, 7.313193276703852E+01, 3.073705169480054E+02), // Horizons 1; @ 10
        Body("asteroid-vesta", "Vesta", CelestialBodyKind.Asteroid, 17.29, 262.7, "star-sun",
            3.532239247968361E+08, 9.024082812679807E-02, 7.143883419019867E+00, 1.036994723221305E+02, 1.514137211464213E+02, 1.228065352734781E+02), // Horizons 4; @ 10
        Body("asteroid-pallas", "Pallas", CelestialBodyKind.Asteroid, 14.3, 256, "star-sun",
            4.142771255180095E+08, 2.307200431916536E-01, 3.493438927121520E+01, 1.728874763656364E+02, 3.109959338723937E+02, 2.869418697716201E+02), // Horizons 2; @ 10
        Body("asteroid-hygiea", "Hygiea", CelestialBodyKind.Asteroid, 5.8, 217, "star-sun",
            4.712745135685983E+08, 1.058877419330247E-01, 3.824709473331296E+00, 2.830840886677219E+02, 3.126210303786861E+02, 2.787372436041538E+02), // Horizons 10; @ 10
        Body("asteroid-eros", "Eros", CelestialBodyKind.Asteroid, 0.000446, 8.4, "star-sun",
            2.181550339090739E+08, 2.228473301048179E-01, 1.082860806538379E+01, 3.042678371695138E+02, 1.789256166340947E+02, 1.481393001243055E+02), // Horizons 433; @ 10
        Body("asteroid-bennu", "Bennu", CelestialBodyKind.Asteroid, 0.000000489, 0.246, "star-sun",
            1.684351655813307E+08, 2.036725574098492E-01, 6.032991770934427E+00, 1.965922827525695E+00, 6.640181421490291E+01, 1.986844873055856E+02), // Horizons 101955; @ 10
        Body("dwarfplanet-eris", "Eris", CelestialBodyKind.DwarfPlanet, 1108, 1163, "star-sun",
            1.015681061958118E+10, 4.389872929347107E-01, 4.396293749659198E+01, 3.599032360922816E+01, 1.508473665025341E+02, 2.119961125295850E+02), // Horizons 136199; @ 10
        Body("dwarfplanet-haumea", "Haumea", CelestialBodyKind.DwarfPlanet, 267, 780, "star-sun",
            6.447142800559540E+09, 1.936323905471109E-01, 2.820848455039785E+01, 1.217853555096910E+02, 2.405318552137042E+02, 2.238991345965299E+02), // Horizons 136108; @ 10
        Body("dwarfplanet-makemake", "Makemake", CelestialBodyKind.DwarfPlanet, 0, 715, "star-sun",
            6.822616150586024E+09, 1.579709697111034E-01, 2.902414538811226E+01, 7.931602099421195E+01, 2.970622275099964E+02, 1.704604341074447E+02), // Horizons 136472; @ 10
        Body("moon-moon", "Moon", CelestialBodyKind.Moon, 4902.800, 1737.4, "planet-earth",
            3.873487122220327E+05, 4.290634331465896E-02, 4.993556249869892E+00, 3.258718938749879E+02, 1.299954335075912E+02, 1.230386594103252E+02), // Horizons 301 @ 399
        Body("moon-phobos", "Phobos", CelestialBodyKind.Moon, 0.0007087, 11.1, "planet-mars",
            9.378668602601116E+03, 1.540212825799346E-02, 2.563864611700494E+01, 8.310500421060853E+01, 2.931239815057842E+02, 3.557172699634382E+02), // Horizons 401 @ 499
        Body("moon-deimos", "Deimos", CelestialBodyKind.Moon, 0.0000962, 6.2, "planet-mars",
            2.345924875932128E+04, 3.575873039002469E-04, 2.411298486448310E+01, 8.159510936091695E+01, 4.032570455852716E+01, 3.489863864677303E+02), // Horizons 402 @ 499
        Body("moon-io", "Io", CelestialBodyKind.Moon, 5959.92, 1821.6, "planet-jupiter",
            4.220245098859576E+05, 4.081736606504870E-03, 2.229847314657690E+00, 3.383838106307675E+02, 2.381526894300492E+01, 9.742617180387609E+01), // Horizons 501 @ 599
        Body("moon-europa", "Europa", CelestialBodyKind.Moon, 3202.74, 1560.8, "planet-jupiter",
            6.712141528650458E+05, 9.433813933548972E-03, 2.069398442802723E+00, 3.262217726351154E+02, 2.082895390144492E+02, 5.214931748957299E+01), // Horizons 502 @ 599
        Body("moon-ganymede", "Ganymede", CelestialBodyKind.Moon, 9887.83, 2634.1, "planet-jupiter",
            1.070670443640562E+06, 2.307495218888272E-03, 2.343887817917342E+00, 3.390537973152031E+02, 3.829799589866596E+00, 2.173736670265996E+02), // Horizons 503 @ 599
        Body("moon-callisto", "Callisto", CelestialBodyKind.Moon, 7179.29, 2410.3, "planet-jupiter",
            1.882455441317220E+06, 7.153963393973430E-03, 1.952408081904839E+00, 3.367539820629992E+02, 3.370633323575944E+01, 3.285779856938490E+02), // Horizons 504 @ 599
        Body("moon-titan", "Titan", CelestialBodyKind.Moon, 8978.14, 2574.7, "planet-saturn",
            1.221962288618861E+06, 2.865617778215654E-02, 2.770513092548089E+01, 1.690809850583570E+02, 1.783909831358355E+02, 1.958740433528797E+02), // Horizons 606 @ 699
        Body("moon-enceladus", "Enceladus", CelestialBodyKind.Moon, 7.21, 252.1, "planet-saturn",
            2.384125106761872E+05, 5.577634110859848E-03, 2.804392715917929E+01, 1.695341455625112E+02, 2.199987605326117E+02, 4.773818646354461E+01), // Horizons 602 @ 699
        Body("moon-rhea", "Rhea", CelestialBodyKind.Moon, 153.94, 763.8, "planet-saturn",
            5.272179419809371E+05, 1.107299780745230E-03, 2.827707888197157E+01, 1.699609249194820E+02, 1.578765440278622E+02, 2.937630202813617E+02), // Horizons 605 @ 699
        Body("moon-iapetus", "Iapetus", CelestialBodyKind.Moon, 120.51, 734.5, "planet-saturn",
            3.561332156332295E+06, 2.859497471156202E-02, 1.697926773072371E+01, 1.388449692667677E+02, 2.326954144502361E+02, 7.636209235944615E+01), // Horizons 608 @ 699
        Body("moon-miranda", "Miranda", CelestialBodyKind.Moon, 4.3, 235.8, "planet-uranus",
            1.298743526129103E+05, 1.439610049808391E-03, 9.787692746785696E+01, 1.631816516819043E+02, 6.488110989128272E+01, 3.517544344709858E+02), // Horizons 705 @ 799
        Body("moon-ariel", "Ariel", CelestialBodyKind.Moon, 83.5, 578.9, "planet-uranus",
            1.909417275932018E+05, 2.469463048097020E-04, 9.771834720237352E+01, 1.676659579135305E+02, 2.509009554895627E+02, 1.906169207485676E+02), // Horizons 701 @ 799
        Body("moon-umbriel", "Umbriel", CelestialBodyKind.Moon, 85.1, 584.7, "planet-uranus",
            2.660069379772863E+05, 4.041959197344160E-03, 9.771473660364752E+01, 1.677254021463282E+02, 7.211703864440835E+01, 1.074163092256076E+02), // Horizons 702 @ 799
        Body("moon-titania", "Titania", CelestialBodyKind.Moon, 226.9, 788.9, "planet-uranus",
            4.363319386593475E+05, 2.408514452487015E-03, 9.776199330670315E+01, 1.676438477325923E+02, 2.573639961828226E+02, 2.545421395164123E+02), // Horizons 703 @ 799
        Body("moon-oberon", "Oberon", CelestialBodyKind.Moon, 205.3, 761.4, "planet-uranus",
            5.836099958830774E+05, 1.711480401943632E-03, 9.790577251159522E+01, 1.677080974560269E+02, 1.543215568338525E+02, 2.734364540744530E+01), // Horizons 704 @ 799
        Body("moon-triton", "Triton", CelestialBodyKind.Moon, 1427.6, 1353.4, "planet-neptune",
            3.547648310566744E+05, 1.417152443524745E-04, 1.291246945230863E+02, 2.228871584352197E+02, 1.067749644446669E+02, 3.353148321590094E+02), // Horizons 801 @ 899
        Body("moon-charon", "Charon", CelestialBodyKind.Moon, 105.9, 606, "dwarfplanet-pluto",
            1.959576460397477E+04, 1.612543751236213E-04, 1.128877686125903E+02, 2.273930822497982E+02, 1.723141408813631E+02, 2.081801782638388E+01), // Horizons 901 @ 999
        Body("comet-halley", "Halley", CelestialBodyKind.Comet, 0, 5.5, "star-sun",
            2.671756445780256E+09, 9.680383410355424E-01, 1.621940429531902E+02, 5.924293645059564E+01, 1.121437599118313E+02, 1.943462113697966E+02), // Horizons 90000030 @ 10
        Body("station-oneill", "O’Neill Habitat", CelestialBodyKind.Station, 0, 0, Earth,
            100000, 0, 35, 20, 0, 90),
        Body("station-staryard", "Earth Staryard", CelestialBodyKind.Station, 0, 0, Earth,
            42164, 0, 23.4392911, 0, 0, 230),
    ]);

    private static CelestialBody Body(string id, string name, CelestialBodyKind kind, double gm,
        double radius, string parent, double a, double e, double inclination, double node, double periapsis, double mean)
    {
        const double radians = Math.PI / 180;
        return new(id, name, kind, gm, parent,
            new(a, e, inclination * radians, node * radians, periapsis * radians, mean * radians),
            RadiusKm: radius);
    }
}
