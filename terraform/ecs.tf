# ecs.tf
data "aws_iam_role" "ecs_task_execution_role" {
  name = "ecsTaskExecutionRole"

}

data "aws_ecr_repository" "default" {
  name = "cheat-game"
}

data "aws_ecr_image" "cheat_image" {
  repository_name = "${data.aws_ecr_repository.default.name}"
  image_tag = "0.0.5"
}

resource "aws_ecs_cluster" "main" {
  name = "cb-cluster"  
}


resource "aws_ecs_task_definition" "app" {
  family                   = "cb-app-task"
  network_mode             = "awsvpc"
  requires_compatibilities = ["FARGATE"]
  cpu                      = "${var.fargate_cpu}"
  memory                   = "${var.fargate_memory}"  
  execution_role_arn       = "${data.aws_iam_role.ecs_task_execution_role.arn}"
  task_role_arn            = "${data.aws_iam_role.ecs_task_execution_role.arn}"
    container_definitions = <<DEFINITION
[
  {
    "environment": [{
    "name" : "REDIS_SERVER",
    "value" : "${aws_elasticache_cluster.cheat_cache.cache_nodes.0.address}:6379"
     }],
    "essential" : true,
    "image" : "${data.aws_ecr_repository.default.repository_url}:${data.aws_ecr_image.cheat_image.image_tag}",
    "name" : "cheatTheGame",
    "portMappings": [
         {"containerPort" : 8085, "hostPort" : 8085}],
    "logConfiguration" : {
       "logDriver" : "awslogs",
       "options" : {
         "awslogs-group" : "${aws_cloudwatch_log_group.cb_log_group.name}",
         "awslogs-region" : "${var.aws_region}",
         "awslogs-stream-prefix" : "cheatGamePrefix"
        }
      }

}
]

DEFINITION

}

resource "aws_ecs_service" "main" {
  name            = "cb-service"
  cluster         = "${aws_ecs_cluster.main.id}"
  task_definition = "${aws_ecs_task_definition.app.arn}"
  desired_count   = "${var.app_count}"
  launch_type     = "FARGATE"
  
  network_configuration {
    security_groups  = ["${aws_security_group.ecs_tasks.id}"]
    subnets          = ["${aws_subnet.private.0.id}","${aws_subnet.private.1.id}"]
    assign_public_ip = true
  }

  load_balancer {
    target_group_arn = "${aws_alb_target_group.app.id}"
    container_name   = "cheatTheGame"
    container_port   = "${var.app_port}"
  }

  depends_on = [
    "aws_alb_listener.front_end",
  ]
}
