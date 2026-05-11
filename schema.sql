CREATE TABLE users (
  id INT AUTO_INCREMENT PRIMARY KEY,
  username VARCHAR(50) UNIQUE NOT NULL,
  password_hash VARCHAR(255) NOT NULL,
  email VARCHAR(100),
  avatar_color VARCHAR(7) DEFAULT '#3498db',
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE rooms (
  id VARCHAR(36) PRIMARY KEY,
  name VARCHAR(100) NOT NULL,
  owner_id INT REFERENCES users(id),
  max_users INT DEFAULT 8,
  is_active BOOLEAN DEFAULT TRUE,
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE room_members (
  room_id VARCHAR(36) REFERENCES rooms(id),
  user_id INT REFERENCES users(id),
  role ENUM('OWNER','MEMBER','VIEWER') DEFAULT 'MEMBER',
  joined_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (room_id, user_id)
);

CREATE TABLE canvas_snapshots (
  id INT AUTO_INCREMENT PRIMARY KEY,
  room_id VARCHAR(36) REFERENCES rooms(id),
  snapshot_data LONGTEXT,
  version INT NOT NULL,
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE draw_actions (
  id BIGINT AUTO_INCREMENT PRIMARY KEY,
  room_id VARCHAR(36),
  user_id INT,
  action_type VARCHAR(20),
  action_data TEXT,
  timestamp BIGINT NOT NULL
);