using Melanchall.DryWetMidi.Smf;
using Melanchall.DryWetMidi.Smf.Interaction;
using System.Collections.Generic;

namespace MidiConverter
{
    class Program
    {
        static void Main(string[] args)
        {
            var midiFile = MidiFile.Read(args[0]);

            var chords = midiFile.GetChords();
            var tempoMap = midiFile.GetTempoMap();

            List<List<Chord>> tracks = SortChords(chords);

#if DEBUG
            //output some debug data if necessary

            foreach(List<Chord> track in tracks)
            {
                System.Console.WriteLine("--TRACK START--");

                MetricTimeSpan totalTrackTime = new MetricTimeSpan(0);
                foreach (Chord chord in track)
                {
                    MetricTimeSpan chordStartTime = TimeConverter.ConvertTo<MetricTimeSpan>(chord.Time, tempoMap);
                    MetricTimeSpan chordDuration = LengthConverter.ConvertTo<MetricTimeSpan>(chord.Length, chord.Time, tempoMap);
                    MetricTimeSpan chordEndTime = chordStartTime + chordDuration;

                    System.Console.WriteLine("start: " + chordStartTime + " end: " + chordEndTime);

                    totalTrackTime += chordDuration;
                }

                System.Console.WriteLine("--TRACK END--");
                System.Console.WriteLine("TIME ELAPSED: " + totalTrackTime + "\n\n");
            }

#else
            //translate the tracks into code readable by the synth
            string songRepresentation = "{.music_tracks = (struct MusicTrack[]) {\n";

            foreach (List<Chord> track in tracks)
            {
                songRepresentation += TrackToString(track, tempoMap);
                if (tracks[tracks.Count - 1] != track) songRepresentation += ",\n";
            }

            songRepresentation += "\n}, .num_tracks = " + tracks.Count + "}";

            System.Console.Write(songRepresentation);
#endif
        }

        static string TrackToString(List<Chord> track, TempoMap tempoMap)
        {
            string representation = "\t{.music_chords = (struct MusicChord[]) {\n";

            int num_chords = track.Count;

            MetricTimeSpan lastChordEndTime = new MetricTimeSpan(0);
            foreach (Chord chord in track)
            {
                MetricTimeSpan chordStartTime = TimeConverter.ConvertTo<MetricTimeSpan>(chord.Time, tempoMap);
                MetricTimeSpan chordDuration = LengthConverter.ConvertTo<MetricTimeSpan>(chord.Length, chord.Time, tempoMap);
                MetricTimeSpan chordEndTime = chordStartTime + chordDuration;

                //pad the track if necessary with rests
                if(lastChordEndTime < chordStartTime)
                {
                    representation += RestToString(chordStartTime - lastChordEndTime) + ",\n";
                    num_chords++;
                }

                //add the chord's string representation
                representation += ChordToString(chord, tempoMap);
                if (track[track.Count - 1] != chord) representation += ",\n";

                //record the end time for the next chord
                lastChordEndTime = chordEndTime;
            }

            representation += "\n\t}, .playback_type = PLAYBACK_MONO, .length = " + num_chords + "}";

            return representation;
        }

        static string RestToString(MetricTimeSpan restDuration)
        {
            string representation = "\t\t{.music_notes = (struct MusicNote[]) {\n";

            double seconds = restDuration.TotalMicroseconds * MICROSECONDS_TO_SECONDS;

            representation += "\t\t\t{.note = \"S\", .octave = 0, .duration = " + seconds + ", .peak_intensity = 0, .sustain_intensity = 0, .adsr_envelope = (double[]) {0, 0, 0, 0}}\n";

            representation += "\t\t}, .duration = " + seconds + ", .num_notes = 1}";

            return representation;
        }

        static string ChordToString(Chord chord, TempoMap tempoMap)
        {
            string representation = "\t\t{.music_notes = (struct MusicNote[]) {\n";

            //append each note to the chord
            int numNotes = 0;
            foreach (Note note in chord.Notes)
            {
                if (numNotes != 0) representation += ",\n";
                representation +=  NoteToString(note, tempoMap);
                numNotes++;
            }

            representation += "\n\t\t}";

            //find the duration
            MetricTimeSpan chordDuration = LengthConverter.ConvertTo<MetricTimeSpan>(chord.Length, chord.Time, tempoMap);
            double seconds = chordDuration.TotalMicroseconds * MICROSECONDS_TO_SECONDS;

            representation += ", .duration = " + seconds + ", .num_notes = " + numNotes + "}";

            return representation;
        }

        const double INTENSITY_SENSITIVITY = 1.018152;
        const double SUSTAINABILITY = 0.4;

        const double MAX_AD_PERCENT = 0.6;
        const double MIN_AD_PERCENT = 0.1;
        const double AD_SLOPE = (MAX_AD_PERCENT - MIN_AD_PERCENT) / (0 - 127);
        const double AD_PROPORTION = 0.2;
        const double MAX_R_PERCENT = 0.4;
        const double MIN_R_PERCENT = 0.05;
        const double R_SLOPE = (MAX_R_PERCENT - MIN_R_PERCENT) / (0 - 127);

        const double MICROSECONDS_TO_SECONDS = 0.000001;

        static string NoteToString(Note note, TempoMap tempoMap)
        {
            string representation = "\t\t\t{";

            //find the note name
            string name = note.NoteName.ToString();

            //convert the sharp into notation that the synth recognizes
            if (name.Contains("Sharp"))
            {
                name = name[0] + "s";
            }

            representation += ".note = \"" + name + "\"";

            //find the note octave
            int octave = note.Octave;

            representation += ", .octave = " + octave;

            //find the note duration
            MetricTimeSpan noteDuration = LengthConverter.ConvertTo<MetricTimeSpan>(note.Length, note.Time, tempoMap);

            double seconds = noteDuration.TotalMicroseconds * MICROSECONDS_TO_SECONDS;

            representation += ", .duration = " + seconds;

            //find the note peak and sustain intensity
            double peakIntensity = System.Math.Pow(INTENSITY_SENSITIVITY, note.Velocity);
            double sustainIntensity = peakIntensity * SUSTAINABILITY;

            representation += ", .peak_intensity = " + peakIntensity + ", .sustain_intensity = " + sustainIntensity;

            //find the adsr envelope
            double adPercent = AD_SLOPE * (System.Math.Abs(note.Velocity) - 127) + MIN_AD_PERCENT;
            double aPercent = adPercent * AD_PROPORTION;
            double dPercent = adPercent - aPercent;
            double rPercent = R_SLOPE * (System.Math.Abs(note.Velocity) - 127) + MIN_R_PERCENT;
            double sPercent = 1 - aPercent - dPercent - rPercent;

            representation += ", .adsr_envelope = (double[]) {" + aPercent + ", " + dPercent + ", " + sPercent + ", " + rPercent + "}";

            representation += "}";
            return representation;
        }

        static List<List<Chord>> SortChords(IEnumerable<Chord> chords)
        {
            //list of all tracks
            List<List<Chord>> tracks = new List<List<Chord>>();
            foreach (Chord chord in chords)
            {
                //sort the chords into separate tracks if they collide with each other
                long chordStartTime = chord.Time;

                //the last chord start and end times
                long lastChordStartTime;
                long lastChordDuration;
                long lastChordEndTime;

                //check each track and add new ones if necessary
                int track_num = 0;
                do
                {
                    //allocate more space if a new track is necessary
                    if (track_num >= tracks.Count)
                    {
                        tracks.Add(new List<Chord>());
                    }

                    //get the track
                    List<Chord> track = tracks[track_num];

                    //get the last chord in the track
                    if (track.Count > 0)
                    {
                        Chord lastChord = track[track.Count - 1];
                        lastChordStartTime = lastChord.Time;
                        lastChordDuration = lastChord.Length;
                        lastChordEndTime = lastChordStartTime + lastChordDuration;

                        if (lastChordEndTime < chordStartTime)
                        {
                            track.Add(chord);
                            break;
                        }
                    }
                    else
                    {
                        track.Add(chord);
                        break;
                    }

                    track_num++;
                } while (true);
            }

            return tracks;
        }
    }
}
