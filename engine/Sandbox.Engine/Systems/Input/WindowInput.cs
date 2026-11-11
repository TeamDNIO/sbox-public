using NativeEngine;
using Sandbox.Utility;
using System.Runtime.InteropServices;

namespace Sandbox.Engine;

/// <summary>
/// Focus, mouse capture and text-input policy for the game and editor play surface.
/// </summary>
[SkipHotload]
internal static class WindowInput
{
	static bool initialized, active, focused, relative, imeAllowed, noIme;
	static IntPtr editorWindow, focusWindow;
	static IntPtr InputWindow => editorWindow != IntPtr.Zero ? editorWindow : GameWindow.Current?.Handle ?? IntPtr.Zero;

	internal static void Initialize()
	{
		if ( Application.IsHeadless ) return;
		initialized = true;
		active = focused = relative = false;
		editorWindow = focusWindow = IntPtr.Zero;
		noIme = CommandLine.HasSwitch( "-noime" );
		imeAllowed = !noIme;
		Sdl.SetHint( "SDL_TOUCH_MOUSE_EVENTS", "0" );
		// Each window owns its close policy, including panels which cancel or hide on close.
		Sdl.SetHint( "SDL_QUIT_ON_LAST_WINDOW_CLOSE", "0" );
		Sdl.SetHint( "SDL_IME_IMPLEMENTED_UI", "composition" );
		if ( !Application.IsHeadless )
		{
			SdlWindow.WarnIfFailed( Sdl.DisableScreenSaver() );
			SdlCursors.Initialize();
		}
		InputSystem.SetGameFocus( focused );
	}

	internal static bool HasMouseFocus() => focused;
	internal static bool IsAppActive() => active;
	internal static bool GetRelativeMouseMode() => relative;
	internal static IntPtr GetEditorMainWindow() => editorWindow;

	internal static void SetEditorMainWindow( IntPtr hwnd )
	{
		InputSystem.SetEditorMainWindow( hwnd );
		editorWindow = GameWindowNative.FromNativeHandle( hwnd );
		SelectX11Input( editorWindow, hwnd );
	}

	[DllImport( "libX11.so.6" )]
	private static extern int XSelectInput( IntPtr display, nuint window, nint eventMask );

	[DllImport( "libX11.so.6" )]
	private static extern int XFlush( IntPtr display );

	[DllImport( "libSDL3.so.0" )]
	[return: MarshalAs( UnmanagedType.U1 )]
	private static extern bool SDL_SetWindowKeyboardGrab( IntPtr window, [MarshalAs( UnmanagedType.U1 )] bool grabbed );

	/// <summary>
	/// On X11, SDL asks for a borrowed window's events in a single request that includes the mouse
	/// button masks. X only lets one client select ButtonPress on a window, and Qt already has, so
	/// the whole request fails and SDL never sees a key press, mouse move or focus change on the
	/// editor's windows - no game input in play mode, and no mouse capture. Ask again on SDL's
	/// connection, without the button masks.
	/// </summary>
	internal static void SelectX11Input( IntPtr sdlWindow, IntPtr xWindow )
	{
		if ( !OperatingSystem.IsLinux() || sdlWindow == IntPtr.Zero )
			return;

		var display = Sdl.GetPointerProperty( Sdl.GetWindowProperties( sdlWindow ), "SDL.window.x11.display", IntPtr.Zero );
		if ( display == IntPtr.Zero )
			return; // not using SDL's X11 driver

		const nint KeyPressMask = 1 << 0, KeyReleaseMask = 1 << 1, EnterWindowMask = 1 << 4, LeaveWindowMask = 1 << 5,
			PointerMotionMask = 1 << 6, KeymapStateMask = 1 << 14, ExposureMask = 1 << 15, StructureNotifyMask = 1 << 17,
			FocusChangeMask = 1 << 21, PropertyChangeMask = 1 << 22;

		XSelectInput( display, (nuint)(nint)xWindow, KeyPressMask | KeyReleaseMask | EnterWindowMask | LeaveWindowMask | PointerMotionMask
			| KeymapStateMask | ExposureMask | StructureNotifyMask | FocusChangeMask | PropertyChangeMask );
		XFlush( display );
	}

	/// <summary>
	/// Relative mouse mode confines the X pointer to the whole editor window, not the play widget, and
	/// X delivers key events to the window under the pointer. Mouse look keeps moving it over other Qt
	/// windows, where presses and releases go to Qt instead of SDL - keys stick down or never register.
	/// While the mouse is captured, have SDL grab the keyboard too so every key reaches the game. It has
	/// to be SDL's grab: SDL re-evaluates its grabs on focus changes and would release one made behind
	/// its back on the same connection.
	/// </summary>
	static void GrabKeyboard( bool grab )
	{
		if ( !OperatingSystem.IsLinux() || editorWindow == IntPtr.Zero )
			return;

		if ( !SDL_SetWindowKeyboardGrab( editorWindow, grab ) )
			Log.Warning( $"Couldn't {(grab ? "grab" : "release")} the keyboard for play mode: {Sdl.GetError()}" );
	}

	internal static void OnEditorGameFocusChange( IntPtr hwnd, bool value )
	{
		if ( !initialized || Application.IsUnitTest ) return;
		focusWindow = value ? GameWindowNative.FromNativeHandle( hwnd ) : IntPtr.Zero;
		UpdateApplicationState( forceFocusUpdate: true );
	}

	/// <summary>
	/// Reads activation from the host and keeps game focus separate from application activation.
	/// </summary>
	internal static void UpdateApplicationState( bool forceFocusUpdate = false )
	{
		if ( !initialized || Application.IsUnitTest ) return;
		var tools = IToolsDll.Current;
		var window = Sdl.GetKeyboardFocus();
		var panelFocused = window != IntPtr.Zero && Sandbox.UI.PanelWindows.Find( window ) is not null;
		var hostActive = tools?.IsApplicationActive ?? (window != IntPtr.Zero);
		var isActive = hostActive || panelFocused;
		var gameWindow = GameWindow.Current?.Handle ?? IntPtr.Zero;
		var hasFocus = hostActive && !panelFocused && (tools is not null ? focusWindow != IntPtr.Zero : gameWindow != IntPtr.Zero && window == gameWindow);
		if ( !forceFocusUpdate && active == isActive && focused == hasFocus ) return;
		active = isActive;
		if ( !active ) Sdl.SetModState( 0 );
		SetFocus( hasFocus );
	}

	static void SetFocus( bool value )
	{
		focused = value;
		InputSystem.SetGameFocus( focused );
		InputRouter.OnWindowActive( focused );
		if ( focused ) SdlCursors.Restore();
		else
		{
			SetRelativeMouseMode( false );
			SdlCursors.ShowArrow();
		}
	}

	internal static void SetRelativeMouseMode( bool value )
	{
		if ( !initialized || Application.IsUnitTest ) return;

		// Focus can change while loading or running editor callbacks, after the last event poll.
		// Releasing capture must remain unconditional, including when SetFocus calls back here.
		if ( value ) UpdateApplicationState();

		var window = InputWindow;
		value &= active && focused;
		var actual = SdlWindow.SetRelativeMouseMode( window, value );
		if ( relative == actual ) return;
		relative = actual;
		GrabKeyboard( relative );
		IToolsDll.Current?.SetRelativeMouseOverride( relative );
	}

	internal static void SetIMEAllowed( bool value )
	{
		if ( !initialized || noIme || imeAllowed == value ) return;
		var window = InputWindow;
		if ( window == IntPtr.Zero ) return;
		if ( OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() )
		{
			if ( !value ) SdlWindow.WarnIfFailed( Sdl.ClearComposition( window ) );
		}
		else if ( !(value ? Sdl.StartTextInput( window ) : Sdl.StopTextInput( window )) )
		{
			Log.Warning( $"Couldn't change SDL text input: {Sdl.GetError()}" );
			return;
		}
		imeAllowed = value;
		if ( !value )
		{
			var area = new Sdl.Rect();
			Sdl.SetTextInputArea( window, ref area, 0 );
		}
	}

	/// <summary>
	/// Set the caret rectangle in SDL window coordinates relative to the game/play window, translating to the editor top-level window when needed.
	/// </summary>
	internal static void SetIMETextLocation( int x, int y, int width, int height )
	{
		if ( !initialized || !imeAllowed || InputWindow == IntPtr.Zero ) return;
		// The editor text-input window is the top level, while the caret is relative to the play widget.
		var surfaceWindow = focusWindow != IntPtr.Zero ? focusWindow : GameWindow.Current?.Handle ?? IntPtr.Zero;
		if ( surfaceWindow != IntPtr.Zero && surfaceWindow != InputWindow )
		{
			if ( !Sdl.GetWindowPosition( surfaceWindow, out var sx, out var sy ) ||
				!Sdl.GetWindowPosition( InputWindow, out var wx, out var wy ) ) return;
			x += sx - wx;
			y += sy - wy;
		}
		var area = new Sdl.Rect { X = x, Y = y, Width = width, Height = height };
		Sdl.SetTextInputArea( InputWindow, ref area, 0 );
	}

	internal static void SetCursorPosition( int x, int y, IntPtr window )
	{
		if ( !initialized ) return;

		UpdateApplicationState();
		if ( !active || !focused ) return;

		if ( window == IntPtr.Zero ) window = focusWindow;
		if ( window != IntPtr.Zero ) Sdl.WarpMouseInWindow( window, x, y );
	}

	internal static void OnWindowDestroyed( IntPtr window )
	{
		if ( !initialized || (window != editorWindow && window != focusWindow) ) return;
		SetFocus( false );
		if ( editorWindow == window ) editorWindow = IntPtr.Zero;
		if ( focusWindow == window ) focusWindow = IntPtr.Zero;
	}

	internal static void Shutdown()
	{
		if ( !initialized ) return;
		SetRelativeMouseMode( false );
		initialized = active = focused = relative = false;
		InputSystem.SetGameFocus( focused );
		editorWindow = focusWindow = IntPtr.Zero;
		SdlCursors.Shutdown();
		if ( !Application.IsHeadless ) SdlWindow.WarnIfFailed( Sdl.EnableScreenSaver() );
	}
}
