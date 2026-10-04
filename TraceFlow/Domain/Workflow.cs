namespace TraceFlow.Domain;

public static class Workflow
{
    public static Station? Next(Station s) => s == Station.Shipped ? null : s + 1;
}
