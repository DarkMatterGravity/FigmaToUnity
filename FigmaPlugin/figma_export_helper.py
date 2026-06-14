"""
Figma Export Helper
A local server that receives files from the Figma plugin and saves them to disk.

Usage:
    python figma_export_helper.py

The export path is set in the Figma plugin UI, not here.
Just run this script and leave it running while you work in Figma.
"""

from http.server import HTTPServer, BaseHTTPRequestHandler
import json
import os
import base64
import shutil

PORT = 9876


class FigmaExportHandler(BaseHTTPRequestHandler):
    def do_OPTIONS(self):
        """Handle CORS preflight requests."""
        self.send_response(200)
        self.send_header('Access-Control-Allow-Origin', '*')
        self.send_header('Access-Control-Allow-Methods', 'POST, OPTIONS')
        self.send_header('Access-Control-Allow-Headers', 'Content-Type')
        self.end_headers()

    def do_POST(self):
        """Handle file export requests from Figma plugin."""
        try:
            content_length = int(self.headers['Content-Length'])
            post_data = self.rfile.read(content_length)
            data = json.loads(post_data)

            export_dir = data.get('exportDir', '')
            files = data.get('files', [])

            if not export_dir:
                raise ValueError("No export directory specified")

            if not files:
                raise ValueError("No files to export")

            print(f"\nExporting {len(files)} files to: {export_dir}")

            # Find and clean _Images folders before writing
            # This ensures deleted Figma elements = deleted image files
            images_folders = set()
            for file in files:
                path_parts = file['path'].split('/')
                if len(path_parts) > 1 and path_parts[0].endswith('_Images'):
                    images_folders.add(path_parts[0])

            for folder_name in images_folders:
                folder_path = os.path.join(export_dir, folder_name)
                if os.path.exists(folder_path):
                    print(f"  Cleaning: {folder_name}/")
                    shutil.rmtree(folder_path)

            # Write all files
            for file in files:
                file_path = os.path.join(export_dir, file['path'])

                # Create directories if needed
                dir_path = os.path.dirname(file_path)
                if dir_path:
                    os.makedirs(dir_path, exist_ok=True)

                # Write the file
                with open(file_path, 'wb') as f:
                    f.write(base64.b64decode(file['base64']))

                print(f"  + {file['path']}")

            # Send success response
            self.send_response(200)
            self.send_header('Content-type', 'application/json')
            self.send_header('Access-Control-Allow-Origin', '*')
            self.end_headers()
            self.wfile.write(json.dumps({
                'status': 'ok',
                'count': len(files),
                'exportDir': export_dir
            }).encode())

            print(f"Done! {len(files)} files saved.")

        except Exception as e:
            print(f"Error: {e}")
            self.send_response(500)
            self.send_header('Content-type', 'application/json')
            self.send_header('Access-Control-Allow-Origin', '*')
            self.end_headers()
            self.wfile.write(json.dumps({
                'status': 'error',
                'message': str(e)
            }).encode())

    def log_message(self, format, *args):
        """Suppress default HTTP logging."""
        pass


def main():
    print("=" * 50)
    print("  Figma Export Helper")
    print("=" * 50)
    print(f"\nListening on: http://localhost:{PORT}")
    print("\nSet your export path in the Figma plugin UI,")
    print("then click 'Save to Folder' to export directly.")
    print("\nPress Ctrl+C to stop.\n")
    print("-" * 50)

    server = HTTPServer(('localhost', PORT), FigmaExportHandler)

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\n\nStopped.")
        server.shutdown()


if __name__ == '__main__':
    main()
