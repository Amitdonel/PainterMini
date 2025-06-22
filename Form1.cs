/* Author: Amit Donel 
 * 
 * PainterMini Application
 * Description: Painter app for rectangles, ellipses, triangles, free lines with DB persistence,
 * undo, restore, resize-safe, professional UI.
 * 
 * Features:
 * - ToolStrip shape and color selection
 * - ColorDialog for color picking
 * - Ctrl+Z undo
 * - Timer-based restore animation
 * - Resizable form, shapes persist
 */

using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PainterMini
{
    public partial class Form1 : Form
    {
        enum ShapeType { Rectangle, Ellipse, Triangle, FreeLine }

        private Bitmap bitmap;
        private ShapeType currentShape = ShapeType.Rectangle;
        private Color fillColor = Color.Transparent;
        private Color borderColor = Color.Black;
        private bool isFilled = false;

        private PainterDataClassesDataContext db = new PainterDataClassesDataContext();

        private List<Point> freeLinePoints = new List<Point>();
        private List<Point> trianglePoints = new List<Point>();

        private Timer restoreTimer = new Timer();
        private Queue<TblShape> restoreQueue = new Queue<TblShape>();

        private ColorDialog colorDialog = new ColorDialog();

        private Point startPoint;
        private Point endPoint;
        private bool isDrawing = false;
        private bool hasMoved = false;

        private ToolStripButton rectButton;
        private ToolStripButton ellipseButton;
        private ToolStripButton triangleButton;
        private ToolStripButton freeLineButton;
        private ToolStripButton toggleFillButton;

        private ComboBox restoreComboBox;
        private Button restoreButton;

        private const int CircleDiameter = 80;
        private const int PenWidth = 2;

        public Form1()
        {
            InitializeComponent();

            this.Paint += Form1_Paint;
            this.KeyPreview = true;
            this.KeyDown += Form1_KeyDown;
            this.Resize += Form1_Resize;

            bitmap = new Bitmap(this.ClientSize.Width, this.ClientSize.Height);

            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            this.UpdateStyles();

            SetupToolStrip();
            SetupRestoreControls();

            this.MouseDown += Form1_MouseDown;
            this.MouseMove += Form1_MouseMove;
            this.MouseUp += Form1_MouseUp;

            restoreTimer.Interval = 300;
            restoreTimer.Tick += RestoreTimer_Tick;
        }

        // Setup ToolStrip with shape and color buttons !
        private void SetupToolStrip()
        {
            ToolStrip toolStrip = new ToolStrip();

            rectButton = new ToolStripButton("Rectangle") { CheckOnClick = true };
            rectButton.Click += (s, e) => SetCurrentShape(ShapeType.Rectangle, rectButton);

            ellipseButton = new ToolStripButton("Ellipse") { CheckOnClick = true };
            ellipseButton.Click += (s, e) => SetCurrentShape(ShapeType.Ellipse, ellipseButton);

            triangleButton = new ToolStripButton("Triangle") { CheckOnClick = true };
            triangleButton.Click += (s, e) => SetCurrentShape(ShapeType.Triangle, triangleButton);

            freeLineButton = new ToolStripButton("FreeLine") { CheckOnClick = true };
            freeLineButton.Click += (s, e) => SetCurrentShape(ShapeType.FreeLine, freeLineButton);

            var fillColorButton = new ToolStripButton("Fill Color");
            fillColorButton.Click += (s, e) =>
            {
                if (colorDialog.ShowDialog() == DialogResult.OK)
                    fillColor = colorDialog.Color;
            };

            var borderColorButton = new ToolStripButton("Border Color");
            borderColorButton.Click += (s, e) =>
            {
                if (colorDialog.ShowDialog() == DialogResult.OK)
                    borderColor = colorDialog.Color;
            };

            toggleFillButton = new ToolStripButton("Toggle Fill - Off");
            toggleFillButton.Click += (s, e) => ToggleFill();

            toolStrip.Items.Add(rectButton);
            toolStrip.Items.Add(ellipseButton);
            toolStrip.Items.Add(triangleButton);
            toolStrip.Items.Add(freeLineButton);
            toolStrip.Items.Add(fillColorButton);
            toolStrip.Items.Add(borderColorButton);
            toolStrip.Items.Add(toggleFillButton);

            this.Controls.Add(toolStrip);
            rectButton.Checked = true;
        }

        // Setup restore combo and button
        private void SetupRestoreControls()
        {
            restoreComboBox = new ComboBox();
            restoreComboBox.Items.AddRange(new string[] { "All", "Rectangle", "Ellipse", "Triangle", "FreeLine" });
            restoreComboBox.SelectedIndex = 0;
            restoreComboBox.Location = new Point(10, 30);
            this.Controls.Add(restoreComboBox);

            restoreButton = new Button();
            restoreButton.Text = "Restore";
            restoreButton.Location = new Point(150, 30);
            restoreButton.Click += (s, e) => StartRestore();
            this.Controls.Add(restoreButton);
        }

        // Update current shape selection
        private void SetCurrentShape(ShapeType shape, ToolStripButton clickedButton)
        {
            currentShape = shape;
            rectButton.Checked = ellipseButton.Checked = triangleButton.Checked = freeLineButton.Checked = false;
            clickedButton.Checked = true;
            Invalidate();
        }

        // Toggle fill on/off
        private void ToggleFill()
        {
            isFilled = !isFilled;
            toggleFillButton.Text = isFilled ? "Toggle Fill - On" : "Toggle Fill - Off";
        }

        // Main paint event
        private void Form1_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.DrawImage(bitmap, 0, 0);
            if (isDrawing) DrawCurrentShapePreview(e.Graphics);
            DrawShapeIndicator(e.Graphics);
        }

        // Draw hollow circle indicator and current shape name if drawing
        private void DrawShapeIndicator(Graphics g)
        {
            Rectangle circleRect = new Rectangle(10, this.ClientSize.Height - CircleDiameter - 10, CircleDiameter, CircleDiameter);
            using (Pen pen = new Pen(Color.Black, PenWidth)) g.DrawEllipse(pen, circleRect);

            if (isDrawing)
            {
                string shapeName = currentShape.ToString();
                using (Font font = new Font("Arial", 10))
                using (Brush brush = new SolidBrush(Color.Black))
                {
                    SizeF textSize = g.MeasureString(shapeName, font);
                    PointF textPos = new PointF(circleRect.X + (circleRect.Width - textSize.Width) / 2,
                                                circleRect.Y + (circleRect.Height - textSize.Height) / 2);
                    g.DrawString(shapeName, font, brush, textPos);
                }
            }
        }

        // Draw preview of current shape while drawing
        private void DrawCurrentShapePreview(Graphics g)
        {
            if (!hasMoved) return;

            using (Pen pen = new Pen(borderColor, PenWidth))
            using (Brush brush = new SolidBrush(fillColor))
            {
                if (currentShape == ShapeType.Rectangle || currentShape == ShapeType.Ellipse)
                {
                    Rectangle rect = GetRectangle(startPoint, endPoint);
                    if (isFilled)
                    {
                        if (currentShape == ShapeType.Rectangle) g.FillRectangle(brush, rect);
                        else g.FillEllipse(brush, rect);
                    }
                    if (currentShape == ShapeType.Rectangle) g.DrawRectangle(pen, rect);
                    else g.DrawEllipse(pen, rect);
                }
                else if (currentShape == ShapeType.FreeLine && freeLinePoints.Count > 1)
                {
                    g.DrawLines(pen, freeLinePoints.ToArray());
                }
            }

            if (currentShape == ShapeType.Triangle && trianglePoints.Count > 1)
            {
                using (Pen pen = new Pen(borderColor, PenWidth))
                {
                    g.DrawLines(pen, trianglePoints.ToArray());
                }
            }
        }

        // Mouse down: start shape
        private void Form1_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                startPoint = e.Location;
                isDrawing = true;
                hasMoved = false;

                if (currentShape == ShapeType.FreeLine)
                {
                    freeLinePoints.Clear();
                    freeLinePoints.Add(e.Location);
                }
                else if (currentShape == ShapeType.Triangle)
                {
                    trianglePoints.Add(e.Location);
                    if (trianglePoints.Count == 3)
                    {
                        DrawAndSaveTriangle();
                        trianglePoints.Clear();
                        Invalidate();
                    }
                }
            }
            Invalidate();
        }

        // Mouse move: update shape
        private void Form1_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDrawing)
            {
                endPoint = e.Location;
                hasMoved = true;
                if (currentShape == ShapeType.FreeLine && e.Button == MouseButtons.Left)
                {
                    freeLinePoints.Add(e.Location);
                }
                Invalidate();
            }
        }

        // Mouse up: finalize shape
        private void Form1_MouseUp(object sender, MouseEventArgs e)
        {
            if (isDrawing)
            {
                endPoint = e.Location;
                if (currentShape == ShapeType.Rectangle || currentShape == ShapeType.Ellipse)
                    DrawAndSaveRectEllipse();
                else if (currentShape == ShapeType.FreeLine && freeLinePoints.Count > 1)
                    DrawAndSaveFreeLine();

                isDrawing = false;
                Invalidate();
            }
        }

        // Draw and save rectangle or ellipse
        private void DrawAndSaveRectEllipse()
        {
            using (Graphics g = Graphics.FromImage(bitmap))
            using (Pen pen = new Pen(borderColor, PenWidth))
            using (Brush brush = new SolidBrush(fillColor))
            {
                Rectangle rect = GetRectangle(startPoint, endPoint);
                if (isFilled)
                {
                    if (currentShape == ShapeType.Rectangle) g.FillRectangle(brush, rect);
                    else g.FillEllipse(brush, rect);
                }
                if (currentShape == ShapeType.Rectangle) g.DrawRectangle(pen, rect);
                else g.DrawEllipse(pen, rect);

                SaveShapeToDb(currentShape.ToString(), ColorToString(fillColor), ColorToString(borderColor),
                    isFilled, $"{rect.X},{rect.Y};{rect.Width},{rect.Height}");
            }
        }

        // Draw and save free line
        private void DrawAndSaveFreeLine()
        {
            using (Graphics g = Graphics.FromImage(bitmap))
            using (Pen pen = new Pen(borderColor, PenWidth))
            {
                g.DrawLines(pen, freeLinePoints.ToArray());
                SaveShapeToDb("FreeLine", ColorToString(fillColor), ColorToString(borderColor),
                    false, string.Join(";", freeLinePoints.Select(p => $"{p.X},{p.Y}")));
            }
        }

        // Draw and save triangle
        private void DrawAndSaveTriangle()
        {
            using (Graphics g = Graphics.FromImage(bitmap))
            using (Pen pen = new Pen(borderColor, PenWidth))
            using (Brush brush = new SolidBrush(fillColor))
            {
                if (isFilled) g.FillPolygon(brush, trianglePoints.ToArray());
                g.DrawPolygon(pen, trianglePoints.ToArray());
            }

            SaveShapeToDb("Triangle", ColorToString(fillColor), ColorToString(borderColor), isFilled,
                string.Join(";", trianglePoints.Select(p => $"{p.X},{p.Y}")));
        }

        // Undo last shape
        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.Z) UndoLastShape();
        }

        // Undo logic
        private void UndoLastShape()
        {
            var lastShape = db.TblShapes.OrderByDescending(s => s.ShapeID).FirstOrDefault();
            if (lastShape != null)
            {
                db.TblShapes.DeleteOnSubmit(lastShape);
                db.SubmitChanges();

                bitmap = new Bitmap(this.ClientSize.Width, this.ClientSize.Height);
                foreach (var shape in db.TblShapes.OrderBy(s => s.ShapeID))
                    DrawShapeFromDb(shape);

                Invalidate();
            }
        }

        // Restore with timer animation
        private void StartRestore()
        {
            bitmap = new Bitmap(this.ClientSize.Width, this.ClientSize.Height);
            var shapes = (restoreComboBox.SelectedItem.ToString() == "All")
                ? db.TblShapes.OrderBy(s => s.ShapeID)
                : db.TblShapes.Where(s => s.ShapeType == restoreComboBox.SelectedItem.ToString()).OrderBy(s => s.ShapeID);

            restoreQueue = new Queue<TblShape>(shapes);
            restoreTimer.Start();
        }

        private void RestoreTimer_Tick(object sender, EventArgs e)
        {
            if (restoreQueue.Count > 0)
            {
                DrawShapeFromDb(restoreQueue.Dequeue());
                Invalidate();
            }
            else
            {
                restoreTimer.Stop();
            }
        }

        private void SaveShapeToDb(string shapeType, string fill, string border, bool filled, string extraData)
        {
            db.TblShapes.InsertOnSubmit(new TblShape
            {
                ShapeType = shapeType,
                FillColor = fill,
                BorderColor = border,
                IsFilled = filled,
                ExtraData = extraData
            });
            db.SubmitChanges();
        }

        private void DrawShapeFromDb(TblShape shape)
        {
            Graphics g = Graphics.FromImage(bitmap);
            Color fill = ParseColor(shape.FillColor, Color.Transparent);
            Color border = ParseColor(shape.BorderColor, Color.Black);

            using (Pen pen = new Pen(border, PenWidth))
            using (Brush brush = new SolidBrush(fill))
            {
                if (shape.ShapeType == "Rectangle" || shape.ShapeType == "Ellipse")
                {
                    Rectangle rect = ParseRectangle(shape.ExtraData);
                    if (shape.IsFilled == true)
                    {
                        if (shape.ShapeType == "Rectangle") g.FillRectangle(brush, rect);
                        else g.FillEllipse(brush, rect);
                    }
                    if (shape.ShapeType == "Rectangle") g.DrawRectangle(pen, rect);
                    else g.DrawEllipse(pen, rect);
                }
                else if (shape.ShapeType == "FreeLine")
                {
                    var pts = ParsePoints(shape.ExtraData);
                    if (pts.Length > 1) g.DrawLines(pen, pts);
                }
                else if (shape.ShapeType == "Triangle")
                {
                    var pts = ParsePoints(shape.ExtraData);
                    if (pts.Length == 3)
                    {
                        if (shape.IsFilled == true) g.FillPolygon(brush, pts);
                        g.DrawPolygon(pen, pts);
                    }
                }
            }
            g.Dispose();
        }

        private string ColorToString(Color c) => $"{c.A},{c.R},{c.G},{c.B}";

        private Color ParseColor(string data, Color fallback)
        {
            try
            {
                var parts = data.Split(',').Select(int.Parse).ToArray();
                return Color.FromArgb(parts[0], parts[1], parts[2], parts[3]);
            }
            catch
            {
                return fallback;
            }
        }

        private Rectangle GetRectangle(Point p1, Point p2) =>
            new Rectangle(Math.Min(p1.X, p2.X), Math.Min(p1.Y, p2.Y), Math.Abs(p1.X - p2.X), Math.Abs(p1.Y - p2.Y));


        private Rectangle ParseRectangle(string data)
        {
            var parts = data.Split(';');
            var position = parts[0].Split(',').Select(int.Parse).ToArray();
            var size = parts[1].Split(',').Select(int.Parse).ToArray();

            return new Rectangle(position[0], position[1], size[0], size[1]);
        }


        private Point[] ParsePoints(string data) =>
            data.Split(';').Select(pt =>
            {
                var xy = pt.Split(',').Select(int.Parse).ToArray();
                return new Point(xy[0], xy[1]);
            }).ToArray();

        // Redraw shapes on resize
        private void Form1_Resize(object sender, EventArgs e)
        {
            if (this.ClientSize.Width > 0 && this.ClientSize.Height > 0)
            {
                bitmap = new Bitmap(this.ClientSize.Width, this.ClientSize.Height);
                foreach (var shape in db.TblShapes.OrderBy(s => s.ShapeID))
                    DrawShapeFromDb(shape);
                Invalidate();
            }
        }
    }
}
