using System.Diagnostics.CodeAnalysis;

namespace Editor;

public class CameraEditorTool : EditorTool<CameraComponent>
{
	CameraToolWindow window;

	/// <summary>
	/// Track when mouse is pressed during look-at mode (prevent exiting mode immediately)
	/// </summary>
	bool lookAtMousePressed = false;

	public override void OnEnabled()
	{
		window = new CameraToolWindow();
		AddOverlay( window, TextFlag.RightBottom, 10 );
	}

	public const string LookAtModeName = "camera.lookat";
	public const string PilotModeName = "camera.pilot";

	public override void OnUpdate()
	{
		window.ToolUpdate();
		AllowGameObjectSelection = EditorToolManager.CurrentModeName != LookAtModeName;

		switch ( EditorToolManager.CurrentModeName )
		{
			case LookAtModeName:
				DoCameraLookAt();
				break;

			case PilotModeName:
				DoCameraPilot();
				break;

			default:
				lookAtMousePressed = false;
				break;
		}
	}

	public override bool ShouldKeepActive()
	{
		return window.ShouldKeepActive();
	}

	public override void OnDisabled()
	{
		if ( EditorToolManager.CurrentModeName == PilotModeName )
		{
			ExitCameraToolMode();
		}
	}

	public override void OnSelectionChanged()
	{
		var camera = GetSelectedComponent<CameraComponent>();
		window.OnSelectionChanged( camera );
	}

	private bool TryFindSelectedCamera( [NotNullWhen( true )] out CameraComponent camera )
	{
		camera = GetSelectedComponent<CameraComponent>();

		if ( camera.IsValid() )
		{
			return true;
		}

		ExitCameraToolMode();
		return false;
	}

	private void ExitCameraToolMode()
	{
		EditorToolManager.CurrentModeName = nameof( ObjectEditorTool );
		SceneViewWidget.Current?.LastSelectedViewportWidget?.SourceCamera = null;
	}

	private void DoCameraLookAt()
	{
		if ( !TryFindSelectedCamera( out var camera ) ) return;

		using ( Gizmo.ObjectScope( camera, Transform.Zero ) )
		{
			var tr = MeshTrace.Run();
			if ( tr.Hit && camera.IsValid() )
			{
				camera.WorldRotation = Rotation.LookAt( tr.HitPosition - camera.WorldPosition );

				using ( Gizmo.Scope( "Aim Handle", new Transform( tr.HitPosition, Rotation.LookAt( tr.Normal ) ) ) )
				{
					Gizmo.Draw.IgnoreDepth = true;
					Gizmo.Draw.Color = Color.White;
					Gizmo.Draw.LineThickness = 2;
					Gizmo.Draw.LineCircle( 0, 8 );
					Gizmo.Draw.Color = Color.White.WithAlpha( 0.5f );
					Gizmo.Draw.LineCircle( 0, 12 );
					Gizmo.Draw.Color = Color.White.WithAlpha( 0.3f );
					Gizmo.Draw.LineCircle( 0, 24 );
					Gizmo.Draw.Color = Color.White.WithAlpha( 0.1f );
					Gizmo.Draw.LineCircle( 0, 48 );
				}
			}

			// Track if mouse was pressed during look-at mode
			if ( Gizmo.WasLeftMousePressed )
			{
				lookAtMousePressed = true;
			}

			// Only exit if mouse was pressed AND released during look-at mode
			if ( lookAtMousePressed && Gizmo.WasLeftMouseReleased )
			{
				ExitCameraToolMode();
			}
		}
	}

	private void DoCameraPilot()
	{
		if ( !TryFindSelectedCamera( out var camera ) ) return;
		if ( SceneViewWidget.Current?.LastSelectedViewportWidget is not { } viewport ) return;

		if ( Application.IsKeyDown( KeyCode.Escape ) )
		{
			ExitCameraToolMode();
			window.OpenWindow();
			return;
		}

		viewport.SourceCamera = camera;
		camera.WorldPosition = viewport.State.CameraPosition;
		camera.WorldRotation = viewport.State.CameraRotation;

		var viewportSize = Gizmo.Camera.Size;
		var viewportRect = new Rect( 0f, 0f, viewportSize.x, viewportSize.y );
		var frameRect = viewportRect.Contain( new Vector2( 1920f, 1080f ), stretch: true );
		var centerThirdX = new Rect( frameRect.Left + frameRect.Width / 3f, frameRect.Top, frameRect.Width / 3f, frameRect.Height );
		var centerThirdY = new Rect( frameRect.Left, frameRect.Top + frameRect.Height / 3f, frameRect.Width, frameRect.Height / 3f );
		var crosshairX = new Rect( frameRect.Center.x - 4f, frameRect.Center.y, 8f, 0f );
		var crosshairY = new Rect( frameRect.Center.x, frameRect.Center.y - 4f, 0f, 8f );

		Gizmo.Draw.ScreenRect( frameRect.Floor(), Color.Transparent, 0f, Color.White, 1f );
		Gizmo.Draw.ScreenRect( centerThirdX.Floor(), Color.Transparent, 0f, Color.White.WithAlpha( 0.125f ), 1f, BlendMode.Lighten );
		Gizmo.Draw.ScreenRect( centerThirdY.Floor(), Color.Transparent, 0f, Color.White.WithAlpha( 0.125f ), 1f, BlendMode.Lighten );
		Gizmo.Draw.ScreenRect( crosshairX.Floor(), Color.Transparent, 0f, Color.White, 1f );
		Gizmo.Draw.ScreenRect( crosshairY.Floor(), Color.Transparent, 0f, Color.White, 1f );
		Gizmo.Draw.ScreenText( "Press Esc to stop piloting", new Vector2( frameRect.Center.x, frameRect.Bottom - 8f ), flags: TextFlag.CenterBottom );
	}
}

class CameraToolWindow : WidgetWindow
{
	private CameraComponent selectedComponent;
	CameraComponent targetComponent;
	SceneWidget SceneWidget;

	private static CameraComponent PinnedCamera;
	private static bool IsPinned;
	internal static bool IsClosed = false;

	public CameraToolWindow()
	{
		ContentMargins = 0;
		Layout = Layout.Column();
		MouseTracking = true;

		Rebuild();
	}

	private IconButton _pinButton;

	void Rebuild()
	{
		if ( IsPinned && PinnedCamera.IsValid() )
		{
			targetComponent = PinnedCamera;
		}

		Layout.Clear( true );
		Layout.Margin = 0;
		Icon = IsClosed ? "" : "photo_camera";
		WindowTitle = IsClosed ? "" : $"Camera Preview - {targetComponent?.GameObject?.Name ?? ""}";
		IsGrabbable = !IsClosed;

		if ( IsPinned )
			WindowTitle += " (Pinned)";

		if ( IsClosed )
		{
			var closedRow = Layout.AddRow();
			closedRow.Add( new IconButton( "photo_camera", OpenWindow ) { ToolTip = "Open Camera Preview", FixedHeight = HeaderHeight, FixedWidth = HeaderHeight, Background = Theme.ControlBackground } );
			return;
		}

		var headerRow = Layout.AddRow();
		headerRow.AddStretchCell();

		_pinButton = new IconButton( IsPinned ? "lock_open" : "lock", TogglePinned )
		{
			ToolTip = IsPinned ? "Unpin" : "Pin",
			FixedHeight = HeaderHeight,
			FixedWidth = HeaderHeight,
			Background = Theme.ControlBackground
		};

		headerRow.Add( _pinButton );
		headerRow.Add( new IconButton( "colorize", LookAt ) { ToolTip = "Look At", FixedHeight = HeaderHeight, FixedWidth = HeaderHeight, Background = Theme.ControlBackground } );
		headerRow.Add( new IconButton( "control_camera", Pilot ) { ToolTip = "Pilot", FixedHeight = HeaderHeight, FixedWidth = HeaderHeight, Background = Theme.ControlBackground } );
		headerRow.Add( new IconButton( "close", CloseWindow ) { ToolTip = "Close Preview", FixedHeight = HeaderHeight, FixedWidth = HeaderHeight, Background = Theme.ControlBackground } );

		SceneWidget = new SceneWidget( this );
		SceneWidget.FixedWidth = PreviewWidth;
		SceneWidget.FixedHeight = PreviewWidth * 9f / 16f;

		if ( targetComponent is CameraComponent camera )
		{
			SceneWidget.Scene = camera.Scene;
		}

		Layout.Add( SceneWidget );
		Layout.Margin = 4;
	}

	static float PreviewWidth = 1280 * 0.4f;

	const float MinPreviewWidth = 160;
	const float ResizeBorder = 6;

	Vector2 resizeEdges;
	Vector2 resizeFrom;
	Rect resizeStartRect;
	Vector2 resizeChrome;
	float resizeStartWidth;

	Vector2 EdgesAt( Vector2 position ) => IsClosed || !SceneWidget.IsValid() ? Vector2.Zero : new Vector2(
		position.x < ResizeBorder ? -1 : position.x > Width - ResizeBorder ? 1 : 0,
		position.y < ResizeBorder ? -1 : position.y > Height - ResizeBorder ? 1 : 0 );

	static CursorShape CursorFor( Vector2 edges ) => edges switch
	{
		{ x: 0, y: 0 } => CursorShape.None,
		{ x: 0 } => CursorShape.SizeV,
		{ y: 0 } => CursorShape.SizeH,
		_ when edges.x == edges.y => CursorShape.SizeFDiag,
		_ => CursorShape.SizeBDiag
	};

	protected override void OnMousePress( MouseEvent e )
	{
		var edges = EdgesAt( e.LocalPosition );
		if ( !e.LeftMouseButton || edges == Vector2.Zero )
		{
			base.OnMousePress( e );
			return;
		}

		resizeEdges = edges;
		resizeFrom = e.ScreenPosition;
		resizeStartRect = new Rect( Position, Size );
		resizeChrome = Size - SceneWidget.Size;
		resizeStartWidth = PreviewWidth;
		Raise();
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		resizeEdges = Vector2.Zero;
		base.OnMouseReleased( e );
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		if ( resizeEdges == Vector2.Zero )
		{
			if ( e.ButtonState == MouseButtons.None )
				Cursor = CursorFor( EdgesAt( e.LocalPosition ) );

			base.OnMouseMove( e );
			return;
		}

		var delta = e.ScreenPosition - resizeFrom;
		var byWidth = delta.x * resizeEdges.x;
		var byHeight = delta.y * resizeEdges.y * 16f / 9f;
		var grow = MathF.Abs( byWidth ) > MathF.Abs( byHeight ) ? byWidth : byHeight;

		var maxWidth = MathF.Min( Parent.Width - resizeChrome.x, (Parent.Height - resizeChrome.y) * 16f / 9f );
		PreviewWidth = Math.Clamp( resizeStartWidth + grow, MinPreviewWidth, MathF.Max( maxWidth, MinPreviewWidth ) );

		SceneWidget.FixedWidth = PreviewWidth;
		SceneWidget.FixedHeight = PreviewWidth * 9f / 16f;
		AdjustSize();

		var x = resizeEdges.x > 0 ? resizeStartRect.Left : resizeStartRect.Right - Width;
		var y = resizeEdges.y > 0 ? resizeStartRect.Top : resizeStartRect.Bottom - Height;
		Position = new Vector2( x, y );
		ConstrainTo( Parent.LocalRect );
	}

	private void TogglePinned()
	{
		SetPinnedState( !IsPinned );
	}

	private void SetPinnedState( bool isPinned )
	{
		if ( IsPinned == isPinned )
			return;

		IsPinned = isPinned;

		_pinButton.Icon = isPinned ? "lock_open" : "lock";
		_pinButton.ToolTip = isPinned ? "Unpin" : "Pin";
		_pinButton.Update();

		UpdateWindowTitle();

		if ( !isPinned && selectedComponent.IsValid() && selectedComponent != targetComponent )
		{
			OnSelectionChanged( selectedComponent );
		}

		if ( isPinned )
		{
			if ( selectedComponent.IsValid() )
			{
				PinnedCamera = selectedComponent;
			}
		}
		else
		{
			if ( !selectedComponent.IsValid() )
			{
				CloseWindow();
			}

			PinnedCamera = null;
		}
	}

	private void SwitchMode( string modeName )
	{
		EditorToolManager.CurrentModeName = modeName;
		// maintain focus on scene even after clicking the button
		SceneViewWidget.Current?.LastSelectedViewportWidget?.Focus();
	}

	void LookAt()
	{
		SwitchMode( CameraEditorTool.LookAtModeName );
	}

	void Pilot()
	{
		if ( targetComponent is not { } camera ) return;
		if ( SceneViewWidget.Current?.LastSelectedViewportWidget is not { } viewport ) return;

		viewport.State.CameraPosition = camera.WorldPosition;
		viewport.State.CameraRotation = camera.WorldRotation;

		SwitchMode( CameraEditorTool.PilotModeName );
		Hide();
	}

	internal void OpenWindow()
	{
		IsClosed = false;
		Rebuild();
		Show();
	}

	void CloseWindow()
	{
		IsClosed = true;
		Release();
		Rebuild();
		Position = Parent.Size - 32;
	}

	public bool ShouldKeepActive()
	{
		return IsPinned && targetComponent.IsValid();
	}

	public void ToolUpdate()
	{
		if ( !targetComponent.IsValid() )
			return;

		if ( SceneWidget.IsValid() )
		{
			targetComponent.UpdateSceneCamera( SceneWidget.Camera );
			SceneWidget.Camera.Rect = new Rect( 0, 0, 1, 1 );
		}
	}

	void UpdateWindowTitle()
	{
		if ( IsClosed )
		{
			WindowTitle = "";
			Update();
			return;
		}

		if ( targetComponent.IsValid() && targetComponent.GameObject.IsValid() )
			WindowTitle = $"Camera Preview - {targetComponent.GameObject.Name}";
		else
			WindowTitle = "Camera Preview";

		if ( IsPinned )
			WindowTitle += " (Pinned)";

		Update();
	}

	internal void OnSelectionChanged( CameraComponent camera )
	{
		selectedComponent = camera;

		if ( camera.IsValid() && camera != PinnedCamera )
		{
			SetPinnedState( false );
		}

		if ( targetComponent.IsValid() )
		{
			// Don't do anything if we're pinned
			if ( IsPinned && (!camera.IsValid() || (targetComponent != camera)) )
				return;
		}

		if ( camera.IsValid() && IsPinned )
		{
			PinnedCamera = camera;
		}

		targetComponent = camera;
		UpdateWindowTitle();

		if ( SceneWidget.IsValid() )
			SceneWidget.Scene = camera.IsValid() ? camera.Scene : null;
	}
}

class SceneWidget : Widget
{
	public Scene Scene { get; set; }
	public SceneCamera Camera { get; set; } = new SceneCamera();

	Pixmap pixmap;

	public SceneWidget( Widget parent ) : base( parent )
	{

	}

	[EditorEvent.Frame]
	internal void Frame()
	{
		if ( !Visible ) return;

		var realSize = Size * DpiScale;

		if ( pixmap is null || pixmap.Size != realSize )
		{
			pixmap = new Pixmap( realSize );
		}

		if ( Scene.IsValid() )
		{
			Camera.World = Scene.SceneWorld;
			Camera.Worlds.Clear();

			Camera.RenderToPixmap( pixmap );
		}

		Update();
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		if ( pixmap is not null )
		{
			Paint.Draw( LocalRect, pixmap );
		}
	}
}
