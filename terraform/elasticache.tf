resource "aws_elasticache_subnet_group" "default" {
  name = "cache-subnet"
  subnet_ids = ["${aws_subnet.private.0.id}","${aws_subnet.private.1.id}"]
}

resource "aws_elasticache_cluster" "cheat_cache" {
  cluster_id           = "cheat-cache"
  engine               = "redis"
  node_type            = "cache.t2.small"
  num_cache_nodes      = 1
  parameter_group_name = "default.redis5.0"
  engine_version       = "5.0.4"
  port                 = 6379
  subnet_group_name   = "${aws_elasticache_subnet_group.default.name}"
  security_group_ids = ["${aws_security_group.elasticache.id}"]
}

