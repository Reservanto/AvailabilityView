namespace AvailabilityView.Repository.Tests;

public sealed class RecordingRulesTests
{
    private const string File = "tests/api-recordings/Segment/GetList/ok.json";

    [Fact]
    public void Recordings_FollowRules()
    {
        string[] files = [.. RepositoryFiles.Current.Text().Where(RecordingRules.AppliesTo)];

        Assert.NotEmpty(files);
        Assert.Empty(files.SelectMany(path => RecordingRules.Check(path, RepositoryFiles.Current.Read(path))));
    }

    [Fact]
    public void Rule_AcceptsCleanRecording()
    {
        Assert.Empty(RecordingRules.Check(File, Recording("""{"Email":null,"Phone":"REDACTED","Name":"Zdroj"}""")));
    }

    [Theory]
    [InlineData("""{"LongTimeToken":"<token>"}""")]
    [InlineData("""{"ShortTimeToken":"<token>"}""")]
    [InlineData("""{"Items":[{"Phone":"+420000000000"}]}""")]
    [InlineData("""{"Items":[{"Address":{"Email":"nekdo"}}]}""")]
    [InlineData("""{"Note":"napiste na nekdo@example.cz"}""")]
    [InlineData("""{"Authorization":"tajne"}""")]
    public void Rule_CatchesForbiddenContent(string body)
    {
        Assert.NotEmpty(RecordingRules.Check(File, Recording(body)));
    }

    [Fact]
    public void Rule_AcceptsTrimmedEvents()
    {
        Assert.Empty(RecordingRules.Check(EventFile, EventRecording(
            """{"IsError":false,"Events":[{"AppointmentId":1,"SourceId":2,"StartDate":3,"EndDate":4,"Status":"Confirmed"}]}""")));
        Assert.Empty(RecordingRules.Check(EventFile, EventRecording(
            """{"IsError":true,"ErrorMessage":"REDACTED","ErrorMessages":["REDACTED"],"ErrorParameters":["Interval"],"Events":null}""")));
    }

    [Theory]
    [InlineData("""{"Events":[{"AppointmentId":1,"CustomerFullName":"Jan"}]}""")]
    [InlineData("""{"Events":[{"SourceId":2,"CustomerNote":null}]}""")]
    [InlineData("""{"Events":[{"StartDate":3,"CustomValues":[]}]}""")]
    [InlineData("""{"Events":[{"Status":"Confirmed","Price":0}]}""")]
    [InlineData("""{"Events":["Jan Novák"]}""")]
    [InlineData("""{"Events":[],"Customers":[]}""")]
    [InlineData("""{"IsError":true,"ErrorMessage":"Jan Novák: alergie","Events":null}""")]
    [InlineData("""{"IsError":true,"ErrorMessages":["REDACTED","Jan Novák"]}""")]
    [InlineData("""{"IsError":true,"ErrorMessage":42}""")]
    [InlineData("""{"IsError":true,"ErrorMessages":"Jan Novák: alergie"}""")]
    [InlineData("""{"IsError":true,"ErrorMessage":["Jan Novák"]}""")]
    [InlineData("""{"IsError":true,"ErrorMessages":[{"Text":"Jan Novák"}]}""")]
    [InlineData("""null""")]
    public void Rule_CatchesEventFieldTheAppDoesNotRead(string body)
    {
        // Rezervace nesou údaje zákazníků. Pole navíc vadí i s hodnotou null.
        Assert.NotEmpty(RecordingRules.Check(EventFile, EventRecording(body)));
    }

    [Theory]
    [InlineData("""{"Items":[{"Id":5,"CustomizedName":"Zaměření 5"}]}""")]
    [InlineData("""{"Items":[{"Id":5,"CustomizedName":""},{"Id":6,"CustomizedName":null},{"Id":7}]}""")]
    public void Rule_AcceptsNeutralOrEmptyNames(string body)
    {
        Assert.Empty(RecordingRules.Check(File, Recording(body)));
    }

    [Theory]
    [InlineData("""{"Items":[{"Id":5,"CustomizedName":"Skutečné zaměření"}]}""")]
    [InlineData("""{"Items":[{"Id":5,"CustomizedName":"Zaměření 6"}]}""")]
    [InlineData("""{"Items":[{"Id":5,"CustomizedName":42}]}""")]
    public void Rule_CatchesNameThatIsNotNeutral(string body)
    {
        Assert.NotEmpty(RecordingRules.Check(File, Recording(body)));
    }

    [Theory]
    [InlineData("""{"Street":"Ulice 1","City":"Město","ZipCode":"000 00"}""", true)]
    [InlineData("""{"Street":"Dlouhá 12","City":"Město","ZipCode":"000 00"}""", false)]
    [InlineData("""{"Street":"Ulice 1","City":"Brno","ZipCode":"000 00"}""", false)]
    [InlineData("""{"Name":null,"State":null,"Latitude":null,"Longitude":null}""", true)]
    [InlineData("""{"Name":"Areál Pod Lesem"}""", false)]
    [InlineData("""{"State":"Liberecký kraj"}""", false)]
    [InlineData("""{"Latitude":50.7663,"Longitude":15.0543}""", false)]
    public void Rule_ChecksNestedAddress(string address, bool clean)
    {
        string recording = """{"endpoint":"Location/GetList","scenario":"ok","request":{},"response":{"status":200,"body":{"Items":[{"Id":2,"Name":"Provozovna 2","Address":"""
            + address + "}]}}}";

        Assert.Equal(clean, RecordingRules.Check("tests/api-recordings/Location/GetList/ok.json", recording).Count == 0);
    }

    [Theory]
    [InlineData("""{"CalendarId":1234,"CalendarName":"Kalendář 1234","BookingUrl":"https://booking.reservanto.cz/Modal?id=1234"}""", true)]
    [InlineData("""{"CalendarId":5678,"CalendarName":"Kalendář 5678","BookingUrl":"https://booking.reservanto.cz/Modal?id=1234"}""", false)]
    [InlineData("""{"CalendarId":1234,"CalendarName":"Kalendář 1234","BookingUrl":"https://booking.reservanto.cz/Modal?id=5678&appId=9"}""", false)]
    [InlineData("""{"CalendarId":"1234","CalendarName":"Kalendář 1234"}""", false)]
    public void Rule_ChecksCalendarOfClasses(string item, bool clean)
    {
        string recording = """{"endpoint":"Classes/GetAvailableAppointments","scenario":"ok","request":{},"response":{"status":200,"body":{"Items":["""
            + item + "]}}}";

        Assert.Equal(clean, RecordingRules.Check("tests/api-recordings/Classes/GetAvailableAppointments/ok.json", recording).Count == 0);
    }

    [Theory]
    [InlineData("""{"CalendarId":null,"LocationId":2023}""", true)]
    [InlineData("""{"CalendarId":1234,"LocationId":2023}""", true)]
    [InlineData("""{"CalendarId":5678,"LocationId":2023}""", false)]
    public void Rule_ChecksCalendarInRequest(string request, bool clean)
    {
        string recording = """{"endpoint":"Classes/GetAvailableAppointments","scenario":"ok","request":"""
            + request + ""","response":{"status":200,"body":{"Items":[]}}}""";

        Assert.Equal(clean, RecordingRules.Check("tests/api-recordings/Classes/GetAvailableAppointments/ok.json", recording).Count == 0);
    }

    [Fact]
    public void Rule_CatchesFileInWrongPlace()
    {
        Assert.NotEmpty(RecordingRules.Check("tests/api-recordings/Location/GetList/ok.json", Recording("{}")));
    }

    [Theory]
    [InlineData("tests/api-recordings/Segment/GetList/ok.json", true)]
    [InlineData("tests/api-recordings/README.md", false)]
    [InlineData("tests/jine/ok.json", false)]
    public void Rule_AppliesOnlyToRecordings(string path, bool expected)
    {
        Assert.Equal(expected, RecordingRules.AppliesTo(path));
    }

    private const string EventFile = "tests/api-recordings/Event/GetList/ok.json";

    private static string EventRecording(string body) =>
        """{"endpoint":"Event/GetList","scenario":"ok","request":{},"response":{"status":200,"body":""" + body + "}}";

    private static string Recording(string body) =>
        """{"endpoint":"Segment/GetList","scenario":"ok","request":{},"response":{"status":200,"body":""" + body + "}}";
}
