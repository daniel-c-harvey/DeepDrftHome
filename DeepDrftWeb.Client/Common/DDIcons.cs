namespace DeepDrftWeb.Client.Common;

public static class DDIcons
{
    /// <summary>
    /// Charleston gas lamp lantern - uses currentColor for theming
    /// </summary>
    /// <summary>
    /// Charleston gas lamp lantern - uses currentColor for theming
    /// </summary>
    public const string GasLamp = """
                                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
                                    <path fill="currentColor" d="M11 0h2v2h-2zM5 6l7-4 7 4v2H5zM6 8h12l-1.5 10h-9zM7.7 9l1.2 8h6.2l1.2-8zM9 19h6v1H9zM10 21h4v2h-4z"/>
                                  </svg>
                                  """;

    /// <summary>
    /// Charleston gas lamp with lit flame - for dark mode
    /// </summary>
    public const string GasLampLit = """
                                     <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
                                       <path fill="currentColor" d="M11 0h2v2h-2zM5 6l7-4 7 4v2H5zM6 8h12l-1.5 10h-9zM7.7 9l1.2 8h6.2l1.2-8zM9 19h6v1H9zM10 21h4v2h-4z"/>
                                       <ellipse cx="12" cy="13" rx="2.5" ry="3.5" fill="#FF9800"/>
                                       <ellipse cx="12" cy="12.5" rx="1.5" ry="2.5" fill="#FFCA28"/>
                                       <ellipse cx="12" cy="12" rx=".7" ry="1.5" fill="#FFF8E1"/>
                                     </svg>
                                     """;
}