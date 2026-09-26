/**
 * Starfall Lightweight 3D Mesh Viewer (Pure WebGL)
 * Zero external dependencies (no three.js/babylon). 100% offline & local.
 * Features:
 * - OBJ format parsing and normal generation
 * - Orbit camera controls (mouse drag rotate, wheel zoom, touch drag/pinch)
 * - Directional + ambient lighting shader
 * - Canvas PNG snapshot export
 */

class StarfallMeshViewer {
  constructor(canvas) {
    this.canvas = canvas;
    this.gl = canvas.getContext('webgl', { preserveDrawingBuffer: true, antialias: true }) ||
              canvas.getContext('experimental-webgl', { preserveDrawingBuffer: true, antialias: true });
    
    if (!this.gl) {
      console.error('WebGL not supported');
      return;
    }

    this.program = null;
    this.vertexBuffer = null;
    this.normalBuffer = null;
    this.vertexCount = 0;
    
    // Orbit camera parameters
    this.target = [0, 0, 0];
    this.distance = 2.5;
    this.minDistance = 0.5;
    this.maxDistance = 10.0;
    this.theta = Math.PI / 4;   // Horizontal azimuth
    this.phi = Math.PI / 6;     // Vertical elevation
    
    // Interaction states
    this.isDragging = false;
    this.lastMouseX = 0;
    this.lastMouseY = 0;
    this.autoRotate = false;

    this.initShaders();
    this.bindEvents();
    this.render();
  }

  initShaders() {
    const gl = this.gl;
    const vsSource = `
      attribute vec3 aPosition;
      attribute vec3 aNormal;
      uniform mat4 uModel;
      uniform mat4 uView;
      uniform mat4 uProjection;
      uniform mat3 uNormalMatrix;
      varying vec3 vNormal;
      varying vec3 vPosition;
      void main() {
        vec4 pos = uModel * vec4(aPosition, 1.0);
        vPosition = pos.xyz;
        vNormal = normalize(uNormalMatrix * aNormal);
        gl_Position = uProjection * uView * pos;
      }
    `;

    const fsSource = `
      precision mediump float;
      varying vec3 vNormal;
      varying vec3 vPosition;
      uniform vec3 uLightDirection;
      uniform vec3 uBaseColor;
      uniform vec3 uAmbientColor;
      void main() {
        vec3 normal = normalize(vNormal);
        vec3 lightDir = normalize(uLightDirection);
        float diff = max(dot(normal, lightDir), 0.0);
        
        // Subtle secondary fill light from opposite angle
        vec3 fillDir = normalize(vec3(-lightDir.x, -0.5, -lightDir.z));
        float fillDiff = max(dot(normal, fillDir), 0.0) * 0.25;

        vec3 diffuse = uBaseColor * (diff + fillDiff);
        vec3 ambient = uAmbientColor * uBaseColor;
        vec3 color = ambient + diffuse;
        
        gl_FragColor = vec4(color, 1.0);
      }
    `;

    const vs = this.compileShader(gl.VERTEX_SHADER, vsSource);
    const fs = this.compileShader(gl.FRAGMENT_SHADER, fsSource);
    
    this.program = gl.createProgram();
    gl.attachShader(this.program, vs);
    gl.attachShader(this.program, fs);
    gl.linkProgram(this.program);

    if (!gl.getProgramParameter(this.program, gl.LINK_STATUS)) {
      console.error('Program link failed: ' + gl.getProgramInfoLog(this.program));
      return;
    }

    this.attribs = {
      position: gl.getAttribLocation(this.program, 'aPosition'),
      normal: gl.getAttribLocation(this.program, 'aNormal')
    };

    this.uniforms = {
      model: gl.getUniformLocation(this.program, 'uModel'),
      view: gl.getUniformLocation(this.program, 'uView'),
      projection: gl.getUniformLocation(this.program, 'uProjection'),
      normalMatrix: gl.getUniformLocation(this.program, 'uNormalMatrix'),
      lightDirection: gl.getUniformLocation(this.program, 'uLightDirection'),
      baseColor: gl.getUniformLocation(this.program, 'uBaseColor'),
      ambientColor: gl.getUniformLocation(this.program, 'uAmbientColor')
    };
  }

  compileShader(type, source) {
    const gl = this.gl;
    const shader = gl.createShader(type);
    gl.shaderSource(shader, source);
    gl.compileShader(shader);
    if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
      console.error('Shader compile error: ' + gl.getShaderInfoLog(shader));
      gl.deleteShader(shader);
      return null;
    }
    return shader;
  }

  parseOBJ(text) {
    const rawPositions = [];
    const rawNormals = [];
    const faces = [];

    const lines = text.split('\n');
    for (let i = 0; i < lines.length; i++) {
      const line = lines[i].trim();
      if (!line || line.startsWith('#')) continue;
      
      const parts = line.split(/\s+/);
      const cmd = parts[0];

      if (cmd === 'v') {
        rawPositions.push([parseFloat(parts[1]), parseFloat(parts[2]), parseFloat(parts[3])]);
      } else if (cmd === 'vn') {
        rawNormals.push([parseFloat(parts[1]), parseFloat(parts[2]), parseFloat(parts[3])]);
      } else if (cmd === 'f') {
        const poly = [];
        for (let j = 1; j < parts.length; j++) {
          const segs = parts[j].split('/');
          const vIdx = parseInt(segs[0], 10) - 1;
          const nIdx = segs[2] ? parseInt(segs[2], 10) - 1 : -1;
          poly.push({ v: vIdx, n: nIdx });
        }
        // Triangulate convex fan
        for (let j = 1; j < poly.length - 1; j++) {
          faces.push([poly[0], poly[j], poly[j + 1]]);
        }
      }
    }

    if (rawPositions.length === 0) return null;

    // Calculate bounding box and center model
    let minX = Infinity, minY = Infinity, minZ = Infinity;
    let maxX = -Infinity, maxY = -Infinity, maxZ = -Infinity;
    for (const p of rawPositions) {
      if (p[0] < minX) minX = p[0]; if (p[0] > maxX) maxX = p[0];
      if (p[1] < minY) minY = p[1]; if (p[1] > maxY) maxY = p[1];
      if (p[2] < minZ) minZ = p[2]; if (p[2] > maxZ) maxZ = p[2];
    }

    const cx = (minX + maxX) / 2;
    const cy = (minY + maxY) / 2;
    const cz = (minZ + maxZ) / 2;
    const maxSpan = Math.max(maxX - minX, maxY - minY, maxZ - minZ) || 1.0;
    const scale = 1.5 / maxSpan;

    const outPositions = [];
    const outNormals = [];

    for (const tri of faces) {
      // Calculate face normal as fallback if vertex normals absent
      const p0 = rawPositions[tri[0].v];
      const p1 = rawPositions[tri[1].v];
      const p2 = rawPositions[tri[2].v];

      let fn = [0, 1, 0];
      if (p0 && p1 && p2) {
        const ax = p1[0] - p0[0], ay = p1[1] - p0[1], az = p1[2] - p0[2];
        const bx = p2[0] - p0[0], by = p2[1] - p0[1], bz = p2[2] - p0[2];
        const nx = ay * bz - az * by;
        const ny = az * bx - ax * bz;
        const nz = ax * by - ay * bx;
        const len = Math.hypot(nx, ny, nz) || 1.0;
        fn = [nx / len, ny / len, nz / len];
      }

      for (const vert of tri) {
        const p = rawPositions[vert.v];
        outPositions.push((p[0] - cx) * scale, (p[1] - cy) * scale, (p[2] - cz) * scale);
        
        if (vert.n >= 0 && rawNormals[vert.n]) {
          const n = rawNormals[vert.n];
          outNormals.push(n[0], n[1], n[2]);
        } else {
          outNormals.push(fn[0], fn[1], fn[2]);
        }
      }
    }

    return {
      positions: new Float32Array(outPositions),
      normals: new Float32Array(outNormals),
      count: outPositions.length / 3
    };
  }

  loadOBJ(text) {
    const mesh = this.parseOBJ(text);
    if (!mesh) return false;

    const gl = this.gl;
    if (this.vertexBuffer) gl.deleteBuffer(this.vertexBuffer);
    if (this.normalBuffer) gl.deleteBuffer(this.normalBuffer);

    this.vertexBuffer = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, this.vertexBuffer);
    gl.bufferData(gl.ARRAY_BUFFER, mesh.positions, gl.STATIC_DRAW);

    this.normalBuffer = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, this.normalBuffer);
    gl.bufferData(gl.ARRAY_BUFFER, mesh.normals, gl.STATIC_DRAW);

    this.vertexCount = mesh.count;
    this.resetCamera();
    this.render();
    return true;
  }

  resetCamera() {
    this.distance = 2.4;
    this.theta = Math.PI / 4;
    this.phi = Math.PI / 8;
  }

  bindEvents() {
    const c = this.canvas;
    c.addEventListener('mousedown', (e) => {
      this.isDragging = true;
      this.lastMouseX = e.clientX;
      this.lastMouseY = e.clientY;
    });

    window.addEventListener('mousemove', (e) => {
      if (!this.isDragging) return;
      const dx = e.clientX - this.lastMouseX;
      const dy = e.clientY - this.lastMouseY;
      this.lastMouseX = e.clientX;
      this.lastMouseY = e.clientY;

      this.theta -= dx * 0.01;
      this.phi += dy * 0.01;
      const limit = Math.PI / 2 - 0.05;
      this.phi = Math.max(-limit, Math.min(limit, this.phi));
      this.render();
    });

    window.addEventListener('mouseup', () => { this.isDragging = false; });

    c.addEventListener('wheel', (e) => {
      e.preventDefault();
      this.distance += e.deltaY * 0.003;
      this.distance = Math.max(this.minDistance, Math.min(this.maxDistance, this.distance));
      this.render();
    }, { passive: false });

    // Touch events for mobile/tablet orbit
    let touchDist = 0;
    c.addEventListener('touchstart', (e) => {
      if (e.touches.length === 1) {
        this.isDragging = true;
        this.lastMouseX = e.touches[0].clientX;
        this.lastMouseY = e.touches[0].clientY;
      } else if (e.touches.length === 2) {
        touchDist = Math.hypot(
          e.touches[0].clientX - e.touches[1].clientX,
          e.touches[0].clientY - e.touches[1].clientY
        );
      }
    });

    c.addEventListener('touchmove', (e) => {
      if (e.touches.length === 1 && this.isDragging) {
        const dx = e.touches[0].clientX - this.lastMouseX;
        const dy = e.touches[0].clientY - this.lastMouseY;
        this.lastMouseX = e.touches[0].clientX;
        this.lastMouseY = e.touches[0].clientY;
        this.theta -= dx * 0.01;
        this.phi += dy * 0.01;
        this.phi = Math.max(-Math.PI / 2 + 0.05, Math.min(Math.PI / 2 - 0.05, this.phi));
        this.render();
      } else if (e.touches.length === 2) {
        const dist = Math.hypot(
          e.touches[0].clientX - e.touches[1].clientX,
          e.touches[0].clientY - e.touches[1].clientY
        );
        const delta = touchDist - dist;
        touchDist = dist;
        this.distance = Math.max(this.minDistance, Math.min(this.maxDistance, this.distance + delta * 0.01));
        this.render();
      }
    });

    c.addEventListener('touchend', () => { this.isDragging = false; });
  }

  render() {
    const gl = this.gl;
    if (!gl || !this.program || this.vertexCount === 0) return;

    // Resize viewport
    const width = this.canvas.clientWidth;
    const height = this.canvas.clientHeight;
    if (this.canvas.width !== width || this.canvas.height !== height) {
      this.canvas.width = width;
      this.canvas.height = height;
    }
    gl.viewport(0, 0, width, height);

    gl.enable(gl.DEPTH_TEST);
    gl.clearColor(0.95, 0.94, 0.91, 1.0); // Starfall warm paper tone #f3efe4
    gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);

    gl.useProgram(this.program);

    // Compute Camera Eye Position
    const cosPhi = Math.cos(this.phi);
    const sinPhi = Math.sin(this.phi);
    const cosTheta = Math.cos(this.theta);
    const sinTheta = Math.sin(this.theta);

    const eye = [
      this.distance * cosPhi * sinTheta,
      this.distance * sinPhi,
      this.distance * cosPhi * cosTheta
    ];

    const modelMat = this.createIdentityMatrix();
    const viewMat = this.createLookAtMatrix(eye, this.target, [0, 1, 0]);
    const projMat = this.createPerspectiveMatrix(45 * Math.PI / 180, width / height, 0.1, 100.0);
    const normMat = this.createNormalMatrix(modelMat);

    gl.uniformMatrix4fv(this.uniforms.model, false, modelMat);
    gl.uniformMatrix4fv(this.uniforms.view, false, viewMat);
    gl.uniformMatrix4fv(this.uniforms.projection, false, projMat);
    gl.uniformMatrix3fv(this.uniforms.normalMatrix, false, normMat);

    // Starfall palette lighting
    gl.uniform3f(this.uniforms.lightDirection, 0.6, 0.8, 0.5);
    gl.uniform3f(this.uniforms.baseColor, 0.57, 0.48, 0.38);    // Warm stone / ochre #91633d
    gl.uniform3f(this.uniforms.ambientColor, 0.35, 0.38, 0.34); // Balanced ambient

    gl.bindBuffer(gl.ARRAY_BUFFER, this.vertexBuffer);
    gl.enableVertexAttribArray(this.attribs.position);
    gl.vertexAttribPointer(this.attribs.position, 3, gl.FLOAT, false, 0, 0);

    gl.bindBuffer(gl.ARRAY_BUFFER, this.normalBuffer);
    gl.enableVertexAttribArray(this.attribs.normal);
    gl.vertexAttribPointer(this.attribs.normal, 3, gl.FLOAT, false, 0, 0);

    gl.drawArrays(gl.TRIANGLES, 0, this.vertexCount);
  }

  exportPNG(filename = 'starfall_mesh_preview.png') {
    this.render();
    const dataURL = this.canvas.toDataURL('image/png');
    const a = document.createElement('a');
    a.href = dataURL;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
  }

  // --- Minimal Math Matrix Helpers ---
  createIdentityMatrix() {
    return new Float32Array([
      1,0,0,0,
      0,1,0,0,
      0,0,1,0,
      0,0,0,1
    ]);
  }

  createPerspectiveMatrix(fov, aspect, near, far) {
    const f = 1.0 / Math.tan(fov / 2);
    const nf = 1 / (near - far);
    return new Float32Array([
      f / aspect, 0, 0, 0,
      0, f, 0, 0,
      0, 0, (far + near) * nf, -1,
      0, 0, (2 * far * near) * nf, 0
    ]);
  }

  createLookAtMatrix(eye, target, up) {
    let z0 = eye[0] - target[0], z1 = eye[1] - target[1], z2 = eye[2] - target[2];
    let len = Math.hypot(z0, z1, z2) || 1;
    z0 /= len; z1 /= len; z2 /= len;

    let x0 = up[1] * z2 - up[2] * z1, x1 = up[2] * z0 - up[0] * z2, x2 = up[0] * z1 - up[1] * z0;
    len = Math.hypot(x0, x1, x2) || 1;
    x0 /= len; x1 /= len; x2 /= len;

    let y0 = z1 * x2 - z2 * x1, y1 = z2 * x0 - z0 * x2, y2 = z0 * x1 - z1 * x0;

    return new Float32Array([
      x0, y0, z0, 0,
      x1, y1, z1, 0,
      x2, y2, z2, 0,
      -(x0 * eye[0] + x1 * eye[1] + x2 * eye[2]),
      -(y0 * eye[0] + y1 * eye[1] + y2 * eye[2]),
      -(z0 * eye[0] + z1 * eye[1] + z2 * eye[2]),
      1
    ]);
  }

  createNormalMatrix(m) {
    return new Float32Array([
      m[0], m[1], m[2],
      m[4], m[5], m[6],
      m[8], m[9], m[10]
    ]);
  }
}

window.StarfallMeshViewer = StarfallMeshViewer;
