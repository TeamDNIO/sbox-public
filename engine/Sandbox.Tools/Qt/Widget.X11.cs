using System;
using System.Runtime.InteropServices;

namespace Editor
{
	public partial class Widget
	{
		bool x11TransparentForInput;

		void UpdateX11InputShape()
		{
			if ( !OperatingSystem.IsLinux() || !IsWindow || !HasFlag( Flag.WA_WState_Created ) )
				return;

			var window = (uint)_widget.winId().ToInt64();

			if ( x11TransparentForInput )
			{
				X11.SetInputShape( window, [] );
			}
			else if ( inputRegion is not null )
			{
				var scale = DpiScale;
				X11.SetInputShape( window, inputRegion.Select( x => X11.Rectangle.FromRect( x, scale ) ).ToArray() );
			}
			else
			{
				X11.ClearInputShape( window );
			}
		}

		static class X11
		{
			const byte ShapeSet = 0;
			const byte ShapeInput = 2;

			[StructLayout( LayoutKind.Sequential )]
			public struct Rectangle
			{
				public short X, Y;
				public ushort Width, Height;

				public static Rectangle FromRect( Rect rect, float scale )
				{
					var left = (int)MathF.Floor( rect.Left * scale );
					var top = (int)MathF.Floor( rect.Top * scale );
					var right = (int)MathF.Ceiling( rect.Right * scale );
					var bottom = (int)MathF.Ceiling( rect.Bottom * scale );

					return new Rectangle { X = (short)left, Y = (short)top, Width = (ushort)Math.Max( right - left, 0 ), Height = (ushort)Math.Max( bottom - top, 0 ) };
				}
			}

			static IntPtr connection;

			[DllImport( "libxcb.so.1" )]
			static extern IntPtr xcb_connect( IntPtr displayName, IntPtr screen );

			[DllImport( "libxcb.so.1" )]
			static extern int xcb_connection_has_error( IntPtr connection );

			[DllImport( "libxcb.so.1" )]
			static extern int xcb_flush( IntPtr connection );

			[DllImport( "libxcb-shape.so.0" )]
			static extern uint xcb_shape_rectangles( IntPtr connection, byte operation, byte kind, byte ordering, uint window, short x, short y, uint count, Rectangle[] rectangles );

			[DllImport( "libxcb-shape.so.0" )]
			static extern uint xcb_shape_mask( IntPtr connection, byte operation, byte kind, uint window, short x, short y, uint bitmap );

			static bool Connect()
			{
				if ( connection == IntPtr.Zero )
					connection = xcb_connect( IntPtr.Zero, IntPtr.Zero );

				return xcb_connection_has_error( connection ) == 0;
			}

			public static void SetInputShape( uint window, Rectangle[] rectangles )
			{
				if ( !Connect() )
					return;

				xcb_shape_rectangles( connection, ShapeSet, ShapeInput, 0, window, 0, 0, (uint)rectangles.Length, rectangles );
				xcb_flush( connection );
			}

			public static void ClearInputShape( uint window )
			{
				if ( !Connect() )
					return;

				xcb_shape_mask( connection, ShapeSet, ShapeInput, window, 0, 0, 0 );
				xcb_flush( connection );
			}
		}
	}
}
